﻿using AngryMonkey.CloudLogin;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;
using RouteAttribute = Microsoft.AspNetCore.Mvc.RouteAttribute;

namespace CoverboxApp.Main.Controllers
{
    [Route("")]
    public class HomeController : Controller
    {
        CloudLoginClient CloudLogin;
        public HomeController(CloudLoginClient cloudLogin) => CloudLogin = cloudLogin;

        public static string PrivateKey { get; set; } = "MIICdwIBADANBgkqhkiG9w0BAQEFAASCAmEwggJdAgEAAoGBAIiOZwb8gye8UmVTMaq0umJkeb6cisZiJ2QzIvOWivctzy4dolsE2nomVAaEcr2ah2lZUocIFHMzo8f5q7C4tGKMm/ynYPZ88Pz6C9Dj5oyfn14rEiIhs5C/PrpUGcl9kK2AlY+mwMwr4qmF0TJVvAm/MP0wz2jUYKJnLcgh77BtAgMBAAECgYEAgYapPNw5P3CGqyt9WdExVXC+dcmgbEnf2VAT3/80cv6VnMVpIXJ6FRDT9JafCy9PL+MUv5YvZ5Jc0KsGaorYNZsR/EbvhMrB0K0RbbIOv2K9Au6PRGWPMx35a2TlQFLGM08kCrkg2AkcUCLSB5HTP+6cqW3JzEPBssCX5l9SX8ECQQDA1UKdY+FRNFoikGmj/vNbGbsndHU38RCL3509o/rkhpEhPD3i4XSJbEllUITNCriLiU+W+B8knueXx7PciFexAkEAtUnXOI/uSBBBJWGvpD0i2L84CHyn5dpu2Zac+BTmj8FBXFuOXz7xERi3333yJeGUVltOS6sTFtb3GGluebGPfQJBAJY+06t8Ihe6WaxqptTvlb9amhcQxzAyNLk3HvXjKV4bd0LVBEcdcUaNx9YX2ZFFFCssboXrh6Bp63q4T+y5ktECQDn9mN77C5n5uR0gFnNPKypyYJY2ae7Y5MStrSCebvJlO2cz0mMdWzfA1HCldSQw+KZ3JqCF5OFVek1QzIoZBnECQDvGYFz6Lwh/Dmrl6oyrNLTqB4UeRqCkE9sxcWzQv5oDkNeGifH9UXlcDjunkj2A8PTFr6QvTlQasXDAVE/FlLA=";
        public static string PublicKey { get; set; } = "MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQCIjmcG/IMnvFJlUzGqtLpiZHm+nIrGYidkMyLzlor3Lc8uHaJbBNp6JlQGhHK9modpWVKHCBRzM6PH+auwuLRijJv8p2D2fPD8+gvQ4+aMn59eKxIiIbOQvz66VBnJfZCtgJWPpsDMK+KphdEyVbwJvzD9MM9o1GCiZy3IIe+wbQIDAQAB";
        public static UnicodeEncoding Encoder = new();

        [Route("")]
        public IActionResult Index()
        {
            return View();
        }
        [Route("Privacy")]
        public IActionResult Privacy()
        {
            return View();
        }

        public static string Decrypt(string data)
        {
            var rsa = new RSACryptoServiceProvider();
            var dataArray = data.Split(new char[] { ',' });
            byte[] dataByte = new byte[dataArray.Length];
            for (int i = 0; i < dataArray.Length; i++)
            {
                dataByte[i] = Convert.ToByte(dataArray[i]);
            }

            rsa.FromXmlString(PrivateKey);
            var decryptedByte = rsa.Decrypt(dataByte, false);
            return Encoder.GetString(decryptedByte);
        }

        public static string Encrypt(string data)
        {
            var rsa = new RSACryptoServiceProvider();
            rsa.FromXmlString(PublicKey);
            var dataToEncrypt = Encoder.GetBytes(data);
            var encryptedByteArray = rsa.Encrypt(dataToEncrypt, false).ToArray();
            var length = encryptedByteArray.Count();
            var item = 0;
            var sb = new StringBuilder();
            foreach (var x in encryptedByteArray)
            {
                item++;
                sb.Append(x);

                if (item < length)
                    sb.Append(",");
            }

            return sb.ToString();
        }
    }
}