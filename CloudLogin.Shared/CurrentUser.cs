using AngryMonkey.Cloud.Login.DataContract;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AngryMonkey.Cloud.Login
{
    public class CurrentUser
    {
        public CloudUser User { get; set; }
        public bool IsAuthenticated { get; set; }
    }
}
