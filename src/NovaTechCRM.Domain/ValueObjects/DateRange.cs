namespace NovaTechCRM.Domain.ValueObjects;

// NOVA-91: DateRange stores Start/End as DateTime without timezone info.
// Callers on the shipping team pass DateTime.Now (local server time).
// Report callers pass DateTime.UtcNow.
// Mixed. Server in UTC+5, so date boundaries are off by 5 hours for shipping reports.
public readonly struct DateRange
{
    public DateTime Start { get; }
    public DateTime End { get; }

    public DateRange(DateTime start, DateTime end)
    {
        if (end < start)
            throw new ArgumentException("End must be >= Start");

        Start = start;
        End = end;
    }

    public TimeSpan Duration => End - Start;
    public int TotalDays => (int)(End - Start).TotalDays;

    public bool Contains(DateTime date) => date >= Start && date <= End;

    public bool Overlaps(DateRange other) =>
        Start < other.End && End > other.Start;

    public static DateRange ForMonth(int year, int month) =>
        new(new DateTime(year, month, 1),
            new DateTime(year, month, DateTime.DaysInMonth(year, month), 23, 59, 59));

    // helper used by ShipmentService — note: uses DateTime.Now not UTC
    // this is the bug entry point for NOVA-91
    public static DateRange LastNDays(int days) =>
        new(DateTime.Now.AddDays(-days), DateTime.Now);

    public static DateRange Today() =>
        new(DateTime.Today, DateTime.Today.AddDays(1).AddSeconds(-1));

    // this one IS correct — used by reports
    public static DateRange LastNDaysUtc(int days) =>
        new(DateTime.UtcNow.AddDays(-days), DateTime.UtcNow);

    public override string ToString() =>
        $"{Start:yyyy-MM-dd} to {End:yyyy-MM-dd}";
}
