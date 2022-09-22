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
		public CloudUser() : base("CloudUser", "CloudUser") { }

		public string FirstName { get; set; }
		public string LastName { get; set; }
		public string? DisplayName { get; set; }
		public bool IsLocked { get; set; } = false;
		public string? Username { get; set; }
		public DateOnly? DateOfBirth { get; set; }
		public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.MinValue;
		public DateTimeOffset LastSignedIn { get; set; } = DateTimeOffset.MinValue;

		// Lists

		public List<LoginInput> Inputs { get; set; } = new List<LoginInput>();

		[JsonIgnore]
		public List<LoginInput> EmailAddresses => Inputs.Where(key => key.Format == InputFormat.EmailAddress).ToList();

		[JsonIgnore]
		public List<LoginInput> PhoneNumbers => Inputs.Where(key => key.Format == InputFormat.PhoneNumber).ToList();


		// Ignore

		[JsonIgnore]
		public LoginInput? PrimaryEmailAddress => EmailAddresses?.FirstOrDefault(key => key.IsPrimary);

		[JsonIgnore]
		public LoginInput? PrimaryPhoneNumber => PhoneNumbers.FirstOrDefault(key => key.IsPrimary);

		[JsonIgnore]
		public List<string> Providers => Inputs.SelectMany(input => input.Providers).Select(key => key.Code).Distinct().ToList();
	}

	public record LoginProvider
    {
        public string Code { get; set; } = string.Empty;
        public string Identifier { get; set; } = string.Empty;
    }

	public record LoginInput
	{
		public string Input { get; set; } = string.Empty;
		public InputFormat Format { get; set; } = InputFormat.Other;
		public string? PhoneNumberCountryCode { get; set; }
        public List<LoginProvider> Providers { get; set; } = new List<LoginProvider>();
		public bool IsPrimary { get; set; } = false;
	}
}
