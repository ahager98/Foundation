namespace Foundation.Ccda;

/// <summary>
/// The kind of a body section, recognised by its C-CDA template id, or by its LOINC section code
/// when no known template id is present.
/// </summary>
public enum CcdaSectionKind
{
    /// <summary>Not a recognised section.</summary>
    Unknown = 0,

    /// <summary>Allergies and intolerances.</summary>
    Allergies,

    /// <summary>Medications.</summary>
    Medications,

    /// <summary>Problems.</summary>
    Problems,

    /// <summary>Results.</summary>
    Results,

    /// <summary>Vital signs.</summary>
    VitalSigns,

    /// <summary>Immunizations.</summary>
    Immunizations,

    /// <summary>Procedures.</summary>
    Procedures,

    /// <summary>Encounters.</summary>
    Encounters,

    /// <summary>Plan of treatment (plan of care).</summary>
    PlanOfTreatment,

    /// <summary>Social history.</summary>
    SocialHistory,

    /// <summary>Family history.</summary>
    FamilyHistory,

    /// <summary>Functional status.</summary>
    FunctionalStatus,

    /// <summary>Assessment.</summary>
    Assessment,

    /// <summary>Assessment and plan.</summary>
    AssessmentAndPlan,

    /// <summary>Reason for visit.</summary>
    ReasonForVisit,

    /// <summary>Chief complaint, or chief complaint and reason for visit.</summary>
    ChiefComplaint,

    /// <summary>History of present illness.</summary>
    HistoryOfPresentIllness,

    /// <summary>Past medical history.</summary>
    PastMedicalHistory,

    /// <summary>Payers.</summary>
    Payers,

    /// <summary>Medical equipment.</summary>
    MedicalEquipment,

    /// <summary>Advance directives.</summary>
    AdvanceDirectives,

    /// <summary>Instructions.</summary>
    Instructions,

    /// <summary>Mental status.</summary>
    MentalStatus,

    /// <summary>Nutrition.</summary>
    Nutrition,

    /// <summary>Goals.</summary>
    Goals,

    /// <summary>Health concerns.</summary>
    HealthConcerns,
}
