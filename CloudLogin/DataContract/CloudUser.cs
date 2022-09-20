using AngryMonkey.Cloud.Geography;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Microsoft.Extensions.DependencyInjection.CloudLoginConfiguration;

namespace AngryMonkey.Cloud.Login.DataContract
{
	[JsonConverter(typeof(StringEnumConverter))]
	public enum InputFormat
	{
		EmailAddress,
		PhoneNumber,
		Other
	}

	public record BaseRecord
	{
		internal BaseRecord(string partitionKey, string discriminator)
		{
			PartitionKey = partitionKey;
			Discriminator = discriminator;
		}

		[JsonProperty("id")]
		internal string CosmosId => $"{Discriminator}|{ID}";

		[JsonProperty("ID")]
		public Guid ID { get; set; }
		public string PartitionKey { get; internal set; }
		public string Discriminator { get; internal set; }

		[JsonIgnore]
		internal PartitionKey CosmosPartitionKey => new PartitionKey(PartitionKey);
	}

	public record CloudUser : BaseRecord
	{
		public CloudUser() : base("User", "CloudUser") { }

		public string FirstName { get; set; }
		public string LastName { get; set; }
		public string? DisplayName { get; set; }
		public bool IsLocked { get; set; } = false;
		public string? Username { get; set; }
		public DateOnly? DateOfBirth { get; set; }
		public DateTimeOffset LastSignedIn { get; set; } = DateTimeOffset.MinValue;

		// Lists

		public List<LoginInput> Inputs { get; set; } = new List<LoginInput>();

		[JsonIgnore]
		public List<LoginInput> EmailAddresses => Inputs.Where(key => key.InputFormat == InputFormat.EmailAddress).ToList();

		[JsonIgnore]
		public List<LoginInput> PhoneNumbers => Inputs.Where(key => key.InputFormat == InputFormat.PhoneNumber).ToList();


		// Ignore

		[JsonIgnore]
		public LoginInput? PrimaryEmailAddress => EmailAddresses?.FirstOrDefault(key => key.IsPrimary);

		[JsonIgnore]
		public LoginInput? PrimaryPhoneNumber => PhoneNumbers.FirstOrDefault(key => key.IsPrimary);

		[JsonIgnore]
		public List<string> Providers => Inputs.Where(key => !string.IsNullOrEmpty(key.Provider)).Select(key => key.Provider).ToList();
	}


	public record LoginInput
	{
		public string Input { get; set; } = string.Empty;
		public InputFormat InputFormat { get; set; } = InputFormat.Other;
		public string? PhoneNumberCountryCode { get; set; }
		public string? Provider { get; set; }
		public string? ProviderId { get; set; }
		public bool IsPrimary { get; set; } = false;
	}

	//public record UserEmailAddress
	//{
	//	public string EmailAddress { get; set; } = string.Empty;
	//	public string? Provider { get; set; }
	//	public string? ProviderId { get; set; }
	//	public bool IsPrimary { get; set; } = false;
	//	public bool IsVerified { get; set; }
	//	public string? VerificationCode { get; set; }
	//	public DateTimeOffset? VerificationCodeTime { get; set; }
	//}

	//public record UserPhoneNumber
	//{
	//	public string CountryCode { get; set; } = string.Empty;//LB
	//	public string CountryCallingCode { get; set; } = string.Empty;//961
	//	public string PhoneNumber { get; set; } = string.Empty;
	//	public string FullPhoneNumber
	//	{
	//		get
	//		{
	//			return $"{CountryCallingCode}{PhoneNumber}";
	//		}
	//	}
	//	public string? Provider { get; set; }
	//	public string? ProviderId { get; set; }
	//	public bool IsPrimary { get; set; } = false;
	//	public bool IsVerified { get; set; }
	//	public string? VerificationCode { get; set; }
	//	public DateTimeOffset? VerificationCodeTime { get; set; }
	//}
}
