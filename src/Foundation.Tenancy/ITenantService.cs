namespace Foundation.Tenancy;

public interface ITenantService
{
    /// <summary>Sets the tenant id explicitly — used outside of an HTTP context (migrations, tests).</summary>
    void SetTenantId(string tenantId);

    Task<string> GetTenantIdAsync();
    string GetTenantId();
    Task<string> GetTenantConnectionStringAsync();
    string GetTenantConnectionStringSync();
}
