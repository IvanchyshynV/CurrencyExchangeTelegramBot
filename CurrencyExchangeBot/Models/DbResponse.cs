using System;

public class DbResponse : ApiResponse
{
    public string Currency { get; set; }

    public override string ToString()
    {
        return $"{base.ToString()}  [{Currency}]";
    }
}
