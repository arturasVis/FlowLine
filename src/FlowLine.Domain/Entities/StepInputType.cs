namespace FlowLine.Domain.Entities;

/// <summary>The kind of value an operator records for a <see cref="StepInput"/>.</summary>
public enum StepInputType
{
    /// <summary>Free text / serial number.</summary>
    Text = 0,
    /// <summary>Numeric only (measurement, torque, temperature) — validated as a number.</summary>
    Number = 1,
    /// <summary>A two-way Pass / Fail result.</summary>
    PassFail = 2,
    /// <summary>A tick-list of items (see <see cref="StepInput.Options"/>).</summary>
    Checklist = 3,
}
