using Microsoft.AspNetCore.Http.Metadata;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Foundation.DataAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Foundation.Compliance;

/// <summary>
/// Static HIPAA technical-safeguard check, run at author time and in CI. It reads metadata, source text and
/// configuration only and reports locations, never data values. Heuristic rules (log scan, configuration scan)
/// are deliberately simple and waivable so the gate stays trusted.
/// </summary>
public static partial class HipaaCheck
{
    private static readonly string[] SecretKeyNames =
        ["password", "pwd", "clientsecret", "apikey", "signingkey", "accountkey", "sharedaccesskey", "authtoken", "secret",
         "secretkey", "accesskey", "privatekey", "subscriptionkey", "token", "secretvalue", "passwordvalue"];

    public static HipaaReport Run(HipaaCheckOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxWaiverDays, 1, nameof(options.MaxWaiverDays));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxWaiverDays, 3650, nameof(options.MaxWaiverDays));

        List<HipaaFinding> findings = [];
        List<Type> types = LoadTypes(options.Assemblies, findings).Concat(options.Types).Distinct().ToList();
        List<string> manifest = [];
        List<string> notClassified = [];

        CheckClassification(types, options, findings, manifest, notClassified);
        CheckEndpointAuthorization(types, options, findings);
        CheckLogLeakage(types, options, findings);
        CheckConfigurationAndEgress(options, findings);

        HipaaReport report = ApplyWaivers(findings, manifest, notClassified, options);
        report.TypesScanned = types.Count;
        report.FilesScanned = options.SourceFiles.Count + options.ConfigurationFiles.Count;
        return report;
    }

    // Namespace-qualified, so a finding and its waiver can never match a same-named type in another namespace.
    private static string NameOf(Type t) => t.FullName ?? t.Name;

    // A type that cannot load is never checked, so each failure is reported (assembly name only) rather than dropped.
    private static List<Type> LoadTypes(IEnumerable<Assembly> assemblies, List<HipaaFinding> findings)
    {
        List<Type> loaded = [];
        foreach (Assembly assembly in assemblies)
        {
            Type?[] all;
            try
            {
                all = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                all = ex.Types;
                int missing = ex.Types.Count(static t => t is null);
                findings.Add(new HipaaFinding(HipaaRules.UnloadableType, assembly.GetName().Name ?? "(unnamed assembly)",
                    $"{missing} type(s) could not be loaded and were not checked; fix the load failure so they are scanned"));
            }

            loaded.AddRange(all.OfType<Type>().Where(static t => (t.IsClass && !t.IsAbstract || t.IsValueType && !t.IsEnum && !t.IsPrimitive)
                && (!t.IsNested || t.IsNestedPublic)
                && t.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null));
        }

        return loaded;
    }

    // ---- Classification + retention ---------------------------------------------------------------------

    private static void CheckClassification(
        List<Type> types, HipaaCheckOptions options, List<HipaaFinding> findings, List<string> manifest, List<string> notClassified)
    {
        foreach (Type type in types.Where(static t => !IsController(t)))
        {
            bool sensitive = options.SensitiveTypePattern.IsMatch(type.Name);
            bool anyClassified = false;

            foreach (MemberInfo member in DataMembers(type))
            {
                string location = $"{NameOf(type)}.{member.Name}";
                ClassifiedAttribute? classified = member.GetCustomAttribute<ClassifiedAttribute>();
                NotClassifiedAttribute? exempt = member.GetCustomAttribute<NotClassifiedAttribute>();

                if (classified is not null)
                {
                    anyClassified = true;
                    manifest.Add($"{location} ({classified.Classification})");
                }
                else if (exempt is not null && !string.IsNullOrWhiteSpace(exempt.Reason))
                {
                    notClassified.Add($"{location}: {exempt.Reason.Trim()}");
                }
                else if (sensitive && !IsInfrastructureMember(member, options))
                {
                    findings.Add(new HipaaFinding(HipaaRules.UnclassifiedField, location, exempt is null
                        ? "field on a person, case, note or form type has no [Phi], [Pii] or [NotClassified] marking"
                        : "[NotClassified] has no reason; the reason is mandatory and appears in the report"));
                }
            }

            if (anyClassified)
            {
                RetentionAttribute? retention = type.GetCustomAttribute<RetentionAttribute>();
                if (retention is null)
                    findings.Add(new HipaaFinding(HipaaRules.MissingRetention, NameOf(type),
                        "type holds classified data but declares no [Retention] (years and deletion behavior)"));
                else if (retention.Years <= 0 || string.IsNullOrWhiteSpace(retention.Deletion))
                    findings.Add(new HipaaFinding(HipaaRules.MissingRetention, NameOf(type),
                        "[Retention] must declare a positive number of years and a deletion behavior"));
            }
        }
    }

    // Members declared on the type and on every base class below BaseEntity/object, so a field inherited from an
    // abstract or shared base cannot dodge classification. Public instance properties and fields are both data.
    private static IEnumerable<MemberInfo> DataMembers(Type type)
    {
        HashSet<string> seen = [];
        for (Type? t = type; t is not null && t != typeof(BaseEntity) && !IsFrameworkType(t); t = t.BaseType)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (PropertyInfo p in t.GetProperties(flags).Where(static p => p.GetIndexParameters().Length == 0 && p.CanRead))
            {
                if (seen.Add(p.Name))
                    yield return p;
            }

            foreach (FieldInfo f in t.GetFields(flags))
            {
                if (seen.Add(f.Name))
                    yield return f;
            }
        }
    }

    // Framework base classes (Exception, DbContext, ...) are not the service's data; the walk stops before them.
    // A null namespace is an application type in the global namespace, not a framework type. Namespaces are matched
    // on a boundary ("System" or "System.*"), so an application namespace like "SystemAdmin" is not skipped.
    private static bool IsFrameworkType(Type t) =>
        t == typeof(object) || t.IsPrimitive || t.IsEnum
        || (t.Namespace is { } ns && (IsInNamespace(ns, "System") || IsInNamespace(ns, "Microsoft")));

    private static bool IsInNamespace(string ns, string root) =>
        ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal);

    // Audit/infrastructure columns, ids and navigations carry no PHI of their own.
    private static bool IsInfrastructureMember(MemberInfo member, HipaaCheckOptions options)
    {
        if (member.DeclaringType is { } d && (d == typeof(BaseEntity) || d == typeof(ITenantScoped)))
            return true;

        if (options.ExemptMemberNames.Contains(member.Name))
            return true;

        Type memberType = member is PropertyInfo p ? p.PropertyType : ((FieldInfo)member).FieldType;
        Type element = ElementType(memberType);
        return typeof(BaseEntity).IsAssignableFrom(element);
    }

    private static Type ElementType(Type t)
    {
        if (t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t) && t.IsGenericType)
            return t.GetGenericArguments()[0];
        return Nullable.GetUnderlyingType(t) ?? t;
    }

    // ---- Access control ---------------------------------------------------------------------------------

    private static bool IsController(Type t) => typeof(ControllerBase).IsAssignableFrom(t);

    private static void CheckEndpointAuthorization(List<Type> types, HipaaCheckOptions options, List<HipaaFinding> findings)
    {
        HashSet<Type> classifiedTypes = types.Where(static t => DataMembers(t).Any(static m => m.GetCustomAttribute<ClassifiedAttribute>() is not null)).ToHashSet();
        if (classifiedTypes.Count == 0)
            return;

        foreach (Type controller in types.Where(IsController))
        {
            // Inherited public actions count too (an abstract CRUD base controller); only the members of the
            // framework controller base and object are excluded.
            List<MethodInfo> actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(static m => !m.IsSpecialName
                    && m.DeclaringType is { } declaring && declaring != typeof(object) && declaring.Assembly != typeof(ControllerBase).Assembly
                    && m.GetCustomAttribute<NonActionAttribute>() is null)
                .ToList();

            // An action touches classified data through its parameters, its return type, or a declared
            // [ProducesResponseType]. An untyped result (IActionResult, ActionResult, object) hides what it returns, so
            // in a controller that touches classified data anywhere every untyped action is held to the same rule.
            Dictionary<MethodInfo, bool> typedTouch = actions.ToDictionary(static a => a, a => TouchesClassified(controller, a, classifiedTypes));
            bool controllerTouches = typedTouch.Values.Any(static v => v);

            foreach (MethodInfo action in actions)
            {
                bool touches = typedTouch[action] || (controllerTouches && ReturnsUntyped(action));
                if (touches && !HasExplicitPolicy(controller, action))
                    findings.Add(new HipaaFinding(HipaaRules.EndpointAuthorization, $"{NameOf(controller)}.{action.Name}",
                        "endpoint touches a classified type without an explicit [Authorize(Policy|Roles)] (default-deny; [AllowAnonymous] is rejected)"));
            }
        }

        foreach (Endpoint endpoint in options.Endpoints)
        {
            if (endpoint.Metadata.GetMetadata<MethodInfo>() is not { } handler
                || (handler.DeclaringType is { } owner && IsController(owner)))
                continue; // controller actions are covered above

            // Declared response metadata (Produces<T>) counts as touching, and an untyped handler (IResult, object) hides
            // what it returns, so it is held to the rule whenever the scanned types hold classified data.
            IEnumerable<Type> touched = handler.GetParameters().Select(static p => p.ParameterType).Append(handler.ReturnType)
                .Concat(endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(static m => m.Type).OfType<Type>());
            bool touches = touched.Any(t => ReferencesAny(t, classifiedTypes, [])) || IsUntypedReturn(handler.ReturnType);
            if (touches && !EndpointHasExplicitPolicy(endpoint))
                findings.Add(new HipaaFinding(HipaaRules.EndpointAuthorization,
                    endpoint.DisplayName ?? $"{NameOf(handler.DeclaringType ?? typeof(object))}.{handler.Name}",
                    "minimal-API endpoint touches a classified type without an explicit authorization policy (default-deny; AllowAnonymous is rejected)"));
        }
    }

    private static bool TouchesClassified(Type controller, MethodInfo action, HashSet<Type> classifiedTypes)
    {
        IEnumerable<Type> touched = action.GetParameters().Select(static p => p.ParameterType)
            .Append(action.ReturnType)
            .Concat(action.GetCustomAttributes<ProducesResponseTypeAttribute>(inherit: true).Select(static a => a.Type))
            .Concat(controller.GetCustomAttributes<ProducesResponseTypeAttribute>(inherit: true).Select(static a => a.Type));
        return touched.Any(t => ReferencesAny(t, classifiedTypes, []));
    }

    private static bool ReturnsUntyped(MethodInfo action) => IsUntypedReturn(action.ReturnType);

    // IActionResult, ActionResult, IResult, object, and the object/JSON result types hide the payload type.
    private static bool IsUntypedReturn(Type returnType)
    {
        Type t = returnType;
        if (t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(Task<>) || t.GetGenericTypeDefinition() == typeof(ValueTask<>)))
            t = t.GetGenericArguments()[0];
        return t == typeof(IActionResult) || t == typeof(ActionResult) || t == typeof(object) || t == typeof(IResult)
            || typeof(ObjectResult).IsAssignableFrom(t) || typeof(JsonResult).IsAssignableFrom(t);
    }

    // True when the type is, wraps (array, Task<T>, ActionResult<T>, List<T>...) or transitively exposes through its
    // own members (DTO properties and fields, base classes included) a classified type. The visited set stops cycles.
    private static bool ReferencesAny(Type type, HashSet<Type> targets, HashSet<Type> visited)
    {
        if (!visited.Add(type))
            return false;
        if (targets.Contains(type))
            return true;
        if (type.IsArray)
            return ReferencesAny(type.GetElementType()!, targets, visited);
        if (type.IsGenericType && type.GetGenericArguments().Any(a => ReferencesAny(a, targets, visited)))
            return true;

        return !IsFrameworkType(type) && DataMembers(type).Any(m =>
            ReferencesAny(m is PropertyInfo p ? p.PropertyType : ((FieldInfo)m).FieldType, targets, visited));
    }

    private static bool HasExplicitPolicy(Type controller, MethodInfo action)
    {
        if (action.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null
            || controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null)
            return false;

        IEnumerable<AuthorizeAttribute> attrs = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
        return attrs.Any(static a => !string.IsNullOrWhiteSpace(a.Policy) || !string.IsNullOrWhiteSpace(a.Roles));
    }

    private static bool EndpointHasExplicitPolicy(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
        && endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(static a => !string.IsNullOrWhiteSpace(a.Policy) || !string.IsNullOrWhiteSpace(a.Roles));

    // ---- Minimum necessary / no leakage -----------------------------------------------------------------

    // .LogInformation( ... and the generic .Log(LogLevel..., ...) overload.
    // plus unqualified [LoggerMessage] methods, conventionally named LogXxx and called from the owning class.
    [GeneratedRegex(@"\.Log(?:Trace|Debug|Information|Warning|Error|Critical)?\s*\(|\bLog[A-Z]\w*\s*\(", RegexOptions.None)]
    private static partial Regex LogCall();

    private static void CheckLogLeakage(List<Type> types, HipaaCheckOptions options, List<HipaaFinding> findings)
    {
        string[] names = types.SelectMany(DataMembers)
            .Where(static m => m.GetCustomAttribute<ClassifiedAttribute>() is not null)
            .Select(static m => m.Name).Distinct().ToArray();
        if (names.Length == 0)
            return;

        // Member access (.Name), message templates ({Name} or {name}) and a camelCase local or parameter passed as an
        // argument (name), so a value renamed to the conventional local is still caught.
        string[] camel = names.Select(static n => char.ToLowerInvariant(n[0]) + n[1..]).Distinct().ToArray();
        string any = string.Join('|', names.Concat(camel).Distinct().Select(Regex.Escape));
        string args = string.Join('|', camel.Select(Regex.Escape));
        Regex leaked = new($@"\.({any})\b|\{{({any})[,:}}]|[(,]\s*({args})\s*[,)]", RegexOptions.Compiled);

        foreach (string file in options.SourceFiles)
        {
            string text = File.ReadAllText(file);
            foreach (Match call in LogCall().Matches(text))
            {
                int end = text.IndexOf(';', call.Index);
                string statement = end < 0 ? text[call.Index..] : text[call.Index..end];
                if (leaked.IsMatch(statement))
                {
                    int line = text.AsSpan(0, call.Index).Count('\n') + 1;
                    findings.Add(new HipaaFinding(HipaaRules.LogLeakage, $"{SourcePath(file, options)}:{line}",
                        "log statement references a member classified as PHI or PII"));
                }
            }
        }
    }

    // Relative to SourceRoot when set (so two Service.cs files stay distinct and the location is stable across
    // machines); otherwise the full path.
    private static string SourcePath(string file, HipaaCheckOptions options) =>
        (options.SourceRoot is { Length: > 0 } root ? Path.GetRelativePath(root, file) : Path.GetFullPath(file)).Replace('\\', '/');

    // ---- Transmission/storage security + third-party disclosure ------------------------------------------

    // Schemes that carry data to a third party. Hosts may be templated ({tenant}), and JSON-escaped slashes are accepted.
    [GeneratedRegex(@"\b(https?|wss?|amqps?|sb|mqtts?|ftps?|sftp):\\?/\\?/([^/\\\s""'@]+@)?(\[[0-9A-Fa-f:.]+\]|[A-Za-z0-9._\-{}]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlHost();

    [GeneratedRegex(@"Encrypt\s*=\s*(false|no)\b|TrustServerCertificate\s*=\s*(true|yes)\b|SslMode\s*=\s*(disable|allow|prefer)\b", RegexOptions.IgnoreCase)]
    private static partial Regex InsecureConnection();

    [GeneratedRegex(@"""(?:MinTlsVersion|MinimumTlsVersion|MinTlsVersionRequired)""\s*:\s*""?(?:1\.0|1\.1|Tls10|Tls11|Ssl3)""?|""SslProtocols""\s*:\s*""[^""]*(?:Ssl3|Tls(?!12|13)\b|Tls11)[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex WeakTls();

    [GeneratedRegex(@"""([^""]+)""\s*:\s*""([^""]*)""")]
    private static partial Regex JsonStringPair();

    // A credential embedded in a connection string value, whatever the JSON key is called.
    [GeneratedRegex(@"\b(?:Password|Pwd|AccountKey|SharedAccessKey|SharedAccessSignature)\s*=\s*([^;""\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringSecret();

    private static void CheckConfigurationAndEgress(HipaaCheckOptions options, List<HipaaFinding> findings)
    {
        HashSet<string> hosts = new(StringComparer.OrdinalIgnoreCase);
        foreach (string target in options.EgressTargets)
            hosts.Add(HostOf(target));

        foreach (string file in options.ConfigurationFiles)
        {
            string name = options.SourceRoot is { Length: > 0 } ? SourcePath(file, options) : Path.GetFileName(file);
            string text = File.ReadAllText(file);

            foreach (Match m in UrlHost().Matches(text))
            {
                string scheme = m.Groups[1].Value;
                string host = m.Groups[3].Value;

                // The host is what follows the userinfo, as Uri parses it: api.trusted.example@evil.example is evil.example.
                if (m.Groups[2].Success)
                    findings.Add(new HipaaFinding(HipaaRules.InsecureConfiguration, $"{name}:{host}",
                        "URL embeds credentials (userinfo) in the address; secrets must live in Key Vault (value not shown)"));

                if (host.Contains('{'))
                {
                    findings.Add(new HipaaFinding(HipaaRules.UnlistedEgress, $"{name}:{host}",
                        "templated outbound target cannot be matched to the BAA inventory; list the concrete hosts in EgressTargets"));
                    continue;
                }

                hosts.Add(host);
                if (scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && !BaaInventory.IsLoopback(host))
                    findings.Add(new HipaaFinding(HipaaRules.InsecureConfiguration, $"{name}:{host}",
                        "plain http:// endpoint; TLS 1.2 or higher is required in transit"));
            }

            if (InsecureConnection().IsMatch(text))
                findings.Add(new HipaaFinding(HipaaRules.InsecureConfiguration, name,
                    "connection string disables encryption or certificate validation"));

            if (WeakTls().IsMatch(text))
                findings.Add(new HipaaFinding(HipaaRules.InsecureConfiguration, name,
                    "TLS below 1.2 or legacy SSL protocols enabled"));

            foreach (Match pair in JsonStringPair().Matches(text))
            {
                string key = pair.Groups[1].Value;
                string value = pair.Groups[2].Value;
                if (value.Length > 0
                    && !IsKeyVaultReference(value)
                    && IsSecretKey(key))
                    findings.Add(new HipaaFinding(HipaaRules.InsecureConfiguration, $"{name}:{key}",
                        "secret has a literal value; secrets must live in Key Vault (value not shown)"));

                foreach (Match secret in ConnectionStringSecret().Matches(value))
                {
                    string literal = secret.Groups[1].Value;
                    if (!IsKeyVaultReference(literal) && literal[0] is not ('{' or '<' or '$'))
                        findings.Add(new HipaaFinding(HipaaRules.InsecureConfiguration, $"{name}:{key}",
                            "connection string has a literal credential; secrets must live in Key Vault (value not shown)"));
                }
            }
        }

        foreach (string host in hosts.OrderBy(static h => h, StringComparer.OrdinalIgnoreCase))
        {
            if (!options.BaaInventory.Covers(host))
                findings.Add(new HipaaFinding(HipaaRules.UnlistedEgress, host,
                    "outbound target is not in the Business Associate Agreement inventory"));
        }
    }

    private static bool IsKeyVaultReference(string value) =>
        value.StartsWith("@Microsoft.KeyVault(", StringComparison.OrdinalIgnoreCase);

    // The key must end in a secret name (DbPassword, ClientSecret), so KeySecretName or RequirePasswordComplexity,
    // which hold names and flags rather than secrets, are not flagged.
    private static bool IsSecretKey(string key)
    {
        string letters = new(key.Where(char.IsLetter).ToArray());
        return SecretKeyNames.Any(s => letters.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    }

    // A target is a URL only when it carries "://". Otherwise it is a bare host, optionally with a port
    // ("api.x.com:443", "10.0.0.1:8080", "[::1]:5000"); the port is dropped. A bare IPv6 address is kept whole.
    internal static string HostOf(string target)
    {
        if (target.Contains("://", StringComparison.Ordinal))
            return Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) ? uri.Host : target;

        if (target.StartsWith('[') && target.IndexOf(']') is var close and > 0)
            return target[1..close];

        int colon = target.IndexOf(':');
        return colon >= 0 && colon == target.LastIndexOf(':') ? target[..colon] : target;
    }

    // ---- Waivers ----------------------------------------------------------------------------------------

    private static HipaaReport ApplyWaivers(List<HipaaFinding> findings, List<string> manifest, List<string> notClassified, HipaaCheckOptions options)
    {
        List<HipaaFinding> remaining = [];
        List<HipaaWaiver> applied = [];
        List<HipaaFinding> waiverFindings = [];
        List<HipaaWaiver> honored = [];
        DateOnly latest = options.Today.AddDays(options.MaxWaiverDays);

        foreach (HipaaWaiver w in options.Waivers)
        {
            string where = $"{w.Rule} {w.Location}";
            if (w.Expires < options.Today)
                waiverFindings.Add(new HipaaFinding(HipaaRules.ExpiredWaiver, where,
                    $"waiver expired {w.Expires:yyyy-MM-dd}; it no longer suppresses its finding"));
            else if (string.IsNullOrWhiteSpace(w.Reason) || string.IsNullOrWhiteSpace(w.ApprovedBy))
                waiverFindings.Add(new HipaaFinding(HipaaRules.ExpiredWaiver, where,
                    "waiver has no reason or approver; it is not honored"));
            else if (w.Expires > latest)
                waiverFindings.Add(new HipaaFinding(HipaaRules.ExpiredWaiver, where,
                    $"waiver expires {w.Expires:yyyy-MM-dd}, beyond the {options.MaxWaiverDays}-day maximum; it is not honored"));
            else
                honored.Add(w);
        }

        foreach (HipaaFinding f in findings)
        {
            HipaaWaiver? waiver = honored.FirstOrDefault(w =>
                w.Rule == f.Rule && w.Location.Equals(f.Location, StringComparison.OrdinalIgnoreCase));
            if (waiver is null)
                remaining.Add(f);
            else
                applied.Add(waiver);
        }

        // Ordinal ordering everywhere, so CI and a developer machine (and reflection discovery order) always agree.
        List<HipaaFinding> ordered = remaining.Concat(waiverFindings)
            .OrderBy(static f => f.Rule, StringComparer.Ordinal)
            .ThenBy(static f => f.Location, StringComparer.Ordinal)
            .ToList();
        return new HipaaReport(ordered, applied.Distinct().ToList(), manifest.Order(StringComparer.Ordinal).ToList(),
            notClassified.Order(StringComparer.Ordinal).ToList(), ScopeNotes);
    }

    // Surfaced in every report so what the check does not cover is visible in the evidence, not silent.
    private static readonly string[] ScopeNotes =
    [
        "Authorization: controllers are scanned from assemblies; minimal-API endpoints only when supplied through HipaaCheckOptions.Endpoints.",
        "Authorization: an untyped controller result (IActionResult/object) is held to the same rule only in controllers that touch classified data elsewhere; an untyped minimal-API handler (IResult/object) is held to it whenever any scanned type holds classified data.",
        "Egress: only http(s), ws(s), amqp(s), sb, mqtt(s) and ftp URLs in configuration files are discovered; connection-string hosts and SDK-internal endpoints must be listed in EgressTargets.",
        "Logging: text scan of .Log*( calls and unqualified LogXxx( [LoggerMessage] methods; static Serilog.Log, {@obj} destructuring and statements containing a semicolon in a string are not covered.",
        "Logging: member names are pooled across every classified type, so logging an unrelated member that shares a name (for example Name) is flagged; arguments are matched by member access, template and camelCase argument name only, so a value passed under any other identifier is not detected."
    ];
}
