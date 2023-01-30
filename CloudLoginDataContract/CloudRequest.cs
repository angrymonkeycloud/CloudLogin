﻿namespace CloudLoginDataContract;
public record CloudRequest : BaseRecord
{
    public CloudRequest() : base("CloudRequest", "CloudRequest") { }

    public Guid? ID { get; set; }
    public Guid? userId { get; set; }
}