using Microsoft.AspNetCore.Mvc.Infrastructure;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AngryMonkey.Cloud.Login.DataContract
{
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
	}

	public record User : BaseRecord
	{
		public User() : base("User", "User") { }

		public string FirstName { get; set; }
		public string LastName { get; set; }
		public string? DisplayName { get; set; }
		public string? Username { get; set; }
		public DateOnly? DateOfBirth { get; set; }
		public DateTimeOffset LastSignedIn { get; set; } = DateTimeOffset.MinValue;

		// Lists

		public List<UserEmailAddress>? EmailAddresses { get; set; }
		public List<UserPhoneNumber>? PhoneNumbers { get; set; }

		// Ignore

		[JsonIgnore]
		public UserEmailAddress? PrimaryEmailAddress => EmailAddresses?.FirstOrDefault(key => key.IsPrimary);

		[JsonIgnore]
		public UserPhoneNumber? PrimaryPhoneNumber => PhoneNumbers?.FirstOrDefault(key => key.IsPrimary);

		[JsonIgnore]
		public List<string> Providers
		{
			get
			{
				List<string> providers = new();

				if (EmailAddresses != null)
					providers.AddRange(EmailAddresses.Where(key => !string.IsNullOrEmpty(key.Provider)).Select(key => key.Provider).ToList());

				if (PhoneNumbers != null)
					providers.AddRange(PhoneNumbers.Where(key => !string.IsNullOrEmpty(key.Provider)).Select(key => key.Provider).ToList());

				return providers;
			}
		}
	}

	public record UserEmailAddress
	{
		public string EmailAddress { get; set; } = string.Empty;
		public string? Provider { get; set; }
		public string? ProviderId { get; set; }
		public bool IsPrimary { get; set; } = false;
		public bool IsVerificated { get; set; } = false;
		public string? VerificationCode { get; set; }
	}

	public record UserPhoneNumber
	{
		public string PhoneNumber { get; set; } = string.Empty;
		public string? Provider { get; set; }
		public string? ProviderId { get; set; }
		public bool IsPrimary { get; set; } = false;
	}
}
