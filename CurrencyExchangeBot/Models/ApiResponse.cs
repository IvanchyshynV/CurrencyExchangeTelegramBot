using System;

public class ApiResponse
{
    public string Date { get; set; }
    public string From { get; set; }
    public string To { get; set; }
    public string Amount { get; set; }
    public string Value { get; set; }

    public override string ToString()
    {
        return $"{Amount} {From} = {Value} {To}  ({Date})";
    }
}
