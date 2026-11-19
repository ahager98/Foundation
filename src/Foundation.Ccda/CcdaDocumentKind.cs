namespace Foundation.Ccda;

/// <summary>The C-CDA document type, recognised by the document-level template id.</summary>
public enum CcdaDocumentKind
{
    /// <summary>Not a recognised C-CDA document template.</summary>
    Unknown = 0,

    /// <summary>Continuity of Care Document (CCD).</summary>
    ContinuityOfCareDocument,

    /// <summary>Consultation note.</summary>
    ConsultationNote,

    /// <summary>History and physical.</summary>
    HistoryAndPhysical,

    /// <summary>Diagnostic imaging report.</summary>
    DiagnosticImagingReport,

    /// <summary>Procedure note.</summary>
    ProcedureNote,

    /// <summary>Operative note.</summary>
    OperativeNote,

    /// <summary>Discharge summary.</summary>
    DischargeSummary,

    /// <summary>Progress note.</summary>
    ProgressNote,

    /// <summary>Unstructured document (a <c>nonXMLBody</c> wrapper).</summary>
    UnstructuredDocument,

    /// <summary>Transfer summary.</summary>
    TransferSummary,

    /// <summary>Referral note.</summary>
    ReferralNote,

    /// <summary>Care plan.</summary>
    CarePlan,
}
