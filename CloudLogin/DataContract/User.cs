using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AngryMonkey.Cloud.Login.DataContract
{
	internal record BaseRecord
	{
		public Guid ID { get; set; }
		public string PartitionKey { get; set; }
		public string Discriminator { get; set; }
	}

	internal record User : BaseRecord
	{
		public string FirstName { get; set; }
		public string LastName { get; set; }
		public string? DisplayName { get; set; }
		public string? Username { get; set; }
		public DateOnly DateOfBirth { get; set; }
		public UserEmailAddress? PrimaryEmailAddress => EmailAddresses.FirstOrDefault(key => key.IsPrimary);
		public UserPhoneNumber? PrimaryPhoneNumber => PhoneNumbers.FirstOrDefault(key => key.IsPrimary);
		public List<UserEmailAddress> EmailAddresses { get; set; } = new();
		public List<UserPhoneNumber> PhoneNumbers { get; set; } = new();

		public List<string> Providers
		{
			get
			{
				List<string> providers = EmailAddresses.Select(key => key.Provider).ToList();

				providers.AddRange(PhoneNumbers.Select(key => key.Provider).ToList());

				return providers;
			}
		}

		internal record UserEmailAddress
		{
			public string EmailAddress { get; set; }
			public string? Provider { get; set; }
			public bool IsPrimary { get; set; } = false;
		}

		internal record UserPhoneNumber
		{
			public string PhoneNumber { get; set; }
			public string? Provider { get; set; }
			public bool IsPrimary { get; set; }
		}
	}
}
