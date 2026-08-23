namespace AngryMonkey.CloudLogin.Models;

public class CloudPaymentMethodReferenceModel
{
    public string Provider { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsDefault { get; set; }
}

public static class CloudPaymentMethodReferenceModelExtensions
{
    public static CloudPaymentMethodReferenceModel ToModel(this CloudPaymentMethodReference source) => new()
    {
        Provider = source.Provider,
        Reference = source.Reference,
        DisplayName = source.DisplayName,
        IsDefault = source.IsDefault
    };

    /// <summary>Builds a payload from a model that carries every field itself (nothing on <see cref="CloudPaymentMethodReference"/> is system-owned beyond what the model already tracks).</summary>
    public static CloudPaymentMethodReference ToContract(this CloudPaymentMethodReferenceModel model) => model.ToContract(new CloudPaymentMethodReference(string.Empty, string.Empty));

    public static CloudPaymentMethodReference ToContract(this CloudPaymentMethodReferenceModel model, CloudPaymentMethodReference original) => original with
    {
        Provider = model.Provider,
        Reference = model.Reference,
        DisplayName = model.DisplayName,
        IsDefault = model.IsDefault
    };
}
