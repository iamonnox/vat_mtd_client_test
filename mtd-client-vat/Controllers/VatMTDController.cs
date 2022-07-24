using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Controllers
{
    public class VatMTDController : Controller
    {
        const string api = "vat mtd (v1.0)";
        const string vatObligationsEndpoint = "/VatMTD/VatObligationsCall";
        const string vatViewReturnEndpoint = "/VatMTD/VatReturnCall";
        const string vatLiabilitiesEndpoint = "/VatMTD/VatLiabilitiesCall";
        const string vatReturnsEndpoint = "/VatMTD/VatReturnCall";
        const string vatViewEndpoint = "/VatMTD/VatViewReturnCall";
        const string vatIndexEndpoint = "./";
        const string validateHeadersEndpoint = "/VatMtd/TestFraudRequestHeaders";


        const string JSON_FORMAT = "application/json";

        private readonly ClientSettings _clientSettings;

        public VatMTDController(IOptions<ClientSettings> clientSettingsOptions)
        {
            _clientSettings = clientSettingsOptions.Value;
        }

        public IActionResult Index()
        {
            ViewData["service"] = api;
            ViewData["validateHeadersEndpoint"] = validateHeadersEndpoint;
            ViewData["vatObligationsEndpoint"] = vatObligationsEndpoint;
            ViewData["vatViewReturnEndpoint"] = vatViewReturnEndpoint;
            ViewData["vatLiabilitiesEndpoint"] = vatLiabilitiesEndpoint;
            ViewData["vatReturnsEndpoint"] = vatReturnsEndpoint;
            ViewData["vatViewReturnEndpoint"] = vatViewEndpoint;

            return View();
        }

        class HmrcContent
        {
            public int code { get; set; }
            public string message { get; set; }
        }

        public class VatReturn
        {
            public string periodKey { get; set; }
            public decimal vatDueSales { get; set; }
            public decimal vatDueAcquisitions { get; set; }
            public decimal totalVatDue { get; set; }
            public decimal vatReclaimedCurrPeriod { get; set; }
            public decimal netVatDue { get; set; }
            public decimal totalValueSalesExVAT { get; set; }
            public decimal totalValuePurchasesExVAT { get; set; }
            public decimal totalValueGoodsSuppliedExVAT { get; set; }
            public decimal totalAcquisitionsExVAT { get; set; }
            public bool finalised { get; set; }
        }

        Dictionary<string, string> GetFraudRequestHeaders(int screenWidth, int screenHeight, int screenColourDepth, int screenScalingFactor, int windowWidth, int windowHeight)
        {
            var requestHeaders = new Dictionary<string, string>();

            //>> set by web app:
            requestHeaders.Add("Gov-Client-User-IDs", $"{Uri.EscapeDataString("trade-control")}={Uri.EscapeDataString("mr test")}");
            requestHeaders.Add("Gov-Vendor-License-IDs", "");
            requestHeaders.Add("Gov-Client-Multi-Factor", "");
            //<<

            requestHeaders.Add("Gov-Client-Device-ID", $"{System.Guid.NewGuid()}");
            requestHeaders.Add("Gov-Client-Connection-Method", "WEB_APP_VIA_SERVER");
            requestHeaders.Add("Gov-Vendor-Product-Name", Uri.EscapeDataString(_clientSettings.AppName));
            requestHeaders.Add("Gov-Vendor-Version", $"my-frontend-app=v{_clientSettings.WebAppVersion}&my-serverside-code=v{_clientSettings.SqlSchemaVersion}");

            DateTime utc = DateTime.UtcNow;
            TimeSpan tz = DateTime.Now - DateTime.UtcNow;
            if (tz.Minutes % 15 != 0)
                tz = tz.Ticks > 0 ? tz.Add(new TimeSpan(0, 15 - tz.Minutes % 15, 0)) : tz.Subtract(new TimeSpan(0, 15 - tz.Minutes % 15, 0));

            requestHeaders.Add("Gov-Client-Public-IP-Timestamp", $"{utc.Year}-{utc.Month.ToString("00")}-{utc.Day.ToString("00")}T{utc.Hour.ToString("00")}:{utc.Minute.ToString("00")}:{utc.Second.ToString("00")}.{utc.Millisecond.ToString("000")}Z");
            requestHeaders.Add("Gov-Client-Timezone", $"UTC{(tz.Ticks > 0 ? '+' : '-')}{tz.Hours.ToString("00")}:{tz.Minutes.ToString("00")}");

            string doNotTrack = HttpContext.Request.Headers["DNT"] == "1" ? "true" : "false";
            requestHeaders.Add("Gov-Client-Browser-Do-Not-Track", doNotTrack);

            var userAgent = HttpContext.Request.Headers["User-Agent"];
            requestHeaders.Add("Gov-Client-Browser-JS-User-Agent", userAgent.ToString());

            requestHeaders.Add("Gov-Client-Screens", $"width={screenWidth}&height={screenHeight}&scaling-factor={screenScalingFactor}&colour-depth={screenColourDepth}");
            requestHeaders.Add("Gov-Client-Window-Size", $"width={windowWidth}&height={windowHeight}");

            requestHeaders.Add("Gov-Client-Public-IP", $"{HttpContext.Connection.RemoteIpAddress}");
            requestHeaders.Add("Gov-Client-Public-Port", $"{HttpContext.Connection.RemotePort}");

            string serverIpAddress = _clientSettings.ServerIPAddress;

            requestHeaders.Add("Gov-Vendor-Forwarded", $"by={serverIpAddress}&for={HttpContext.Connection.RemoteIpAddress}");
            requestHeaders.Add("Gov-Vendor-Public-IP", $"{serverIpAddress}");
            

            return (requestHeaders);
        }

        string ServerIPAddress
        {
            get
            {
                var addressList = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList;

                Regex regexIP = new Regex(@"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$");

                var ips = addressList.Where(x => regexIP.Match(x.ToString()).Captures.Count > 0 ).FirstOrDefault();

                return ips?.ToString();
            }
        }

        void AddHmrcSecurityHeader(HttpRequestHeaders requestHeaders, int screenWidth, int screenHeight, int screenColourDepth, int screenScalingFactor, int windowWidth, int windowHeight)
        {
            foreach (var requestHeader in GetFraudRequestHeaders(screenWidth, screenHeight, screenColourDepth, screenScalingFactor, windowWidth, windowHeight))
                requestHeaders.Add(requestHeader.Key, requestHeader.Value);
        }

        public IActionResult ShowFraudRequestHeaders(int sw, int sh, int scd, int ssf, int ww, int wh)
        {
            return Content(JsonSerializer.Serialize(GetFraudRequestHeaders(sw, sh, scd, ssf, ww, wh)), JSON_FORMAT);
        }

        public async Task<IActionResult> TestFraudRequestHeaders(int sw, int sh, int scd, int ssf, int ww, int wh)
        {
            string accessToken = await HttpContext.GetTokenAsync("access_token");

            if (accessToken != null)
            {
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(_clientSettings.Uri);
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.hmrc.1.0+json"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    AddHmrcSecurityHeader(client.DefaultRequestHeaders, sw, sh, scd, ssf, ww, wh);

                    HttpResponseMessage response = await client.GetAsync($"/test/fraud-prevention-headers/validate");

                    String resp = await response.Content.ReadAsStringAsync();
                    return Content(resp, JSON_FORMAT);
                }
            }
            else
            {
                return Challenge(new AuthenticationProperties() { RedirectUri = vatIndexEndpoint }, "HMRC");
            }
        }

        public IActionResult ChallangeCall()
        {
            return Challenge(new AuthenticationProperties() { RedirectUri = vatIndexEndpoint }, "HMRC");
        }

        public async Task<IActionResult> VatObligationsCall(string vn, int sw, int sh, int scd, int ssf, int ww, int wh)
        {
            string accessToken = await HttpContext.GetTokenAsync("access_token");

            if (accessToken != null)
            {
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(_clientSettings.Uri);
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.hmrc.1.0+json"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    AddHmrcSecurityHeader(client.DefaultRequestHeaders, sw, sh, scd, ssf, ww, wh);

                    client.DefaultRequestHeaders.Add("Gov-Test-Scenario", "QUARTERLY_ONE_MET");
                    HttpResponseMessage response = await client.GetAsync($"/organisations/vat/{vn}/obligations?status=O"); //?status=O || F
                    
                    String resp = await response.Content.ReadAsStringAsync();
                    return Content(resp, JSON_FORMAT);
                }
            }
            else
            {
                return Challenge(new AuthenticationProperties() { RedirectUri = vatIndexEndpoint }, "HMRC");
            }
        }

        public async Task<IActionResult> VatViewReturnCall(string vn, string pk, int sw, int sh, int scd, int ssf, int ww, int wh)
        {
            string accessToken = await HttpContext.GetTokenAsync("access_token");

            if (accessToken != null)
            {
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(_clientSettings.Uri);
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.hmrc.1.0+json"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    AddHmrcSecurityHeader(client.DefaultRequestHeaders, sw, sh, scd, ssf, ww, wh);

                    HttpResponseMessage response = await client.GetAsync($"/organisations/vat/{vn}/returns/{pk}");

                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                        return Content(JsonSerializer.Serialize(new HmrcContent() { code = (int)response.StatusCode, message = response.ReasonPhrase }), JSON_FORMAT);
                    else
                        return Content(await response.Content.ReadAsStringAsync(), JSON_FORMAT);
                }
            }
            else
            {
                return Challenge(new AuthenticationProperties() { RedirectUri = vatIndexEndpoint }, "HMRC");
            }
        }

        public async Task<IActionResult> VatLiabilitiesCall(string vn, int sw, int sh, int scd, int ssf, int ww, int wh)
        {
            string accessToken = await HttpContext.GetTokenAsync("access_token");

            if (accessToken != null)
            {
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(_clientSettings.Uri);
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.hmrc.1.0+json"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);                    
                    
                    AddHmrcSecurityHeader(client.DefaultRequestHeaders, sw, sh, scd, ssf, ww, wh);

                    client.DefaultRequestHeaders.Add("Gov-Test-Scenario", "MULTIPLE_LIABILITIES");
                    HttpResponseMessage response = await client.GetAsync($"/organisations/vat/{vn}/liabilities?from=2017-04-05&to=2017-12-21");
                    
                    String resp = await response.Content.ReadAsStringAsync();
                    return Content(resp, JSON_FORMAT);
                }
            }
            else
            {
                return Challenge(new AuthenticationProperties() { RedirectUri = vatIndexEndpoint }, "HMRC");
            }
        }

        public async Task<IActionResult> VatReturnCall(string vn, string pk, int sw, int sh, int scd, int ssf, int ww, int wh)
        {
            VatReturn vatReturn = new()
            {
                periodKey = pk,
                vatDueSales = 105.5M,
                vatDueAcquisitions = -100.45M,
                totalVatDue = 5.05M,
                vatReclaimedCurrPeriod = 105.15M,
                netVatDue = 100.1M,
                totalValueSalesExVAT = 300M,
                totalValuePurchasesExVAT = 300M,
                totalValueGoodsSuppliedExVAT = 3000M,
                totalAcquisitionsExVAT = 3000M,
                finalised = true
            };

            string jsonString = JsonSerializer.Serialize(vatReturn);

            string accessToken = await HttpContext.GetTokenAsync("access_token");

            if (accessToken != null)
            {
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(_clientSettings.Uri);
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.hmrc.1.0+json"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    HttpRequestMessage requestMessage = new()
                    {
                        Content = new StringContent(jsonString),
                        RequestUri = new Uri($"{_clientSettings.Uri}/organisations/vat/{vn}/returns"),
                        Method = HttpMethod.Post
                    };

                    requestMessage.Content.Headers.ContentType = new MediaTypeWithQualityHeaderValue(JSON_FORMAT);

                    AddHmrcSecurityHeader(client.DefaultRequestHeaders, sw, sh, scd, ssf, ww, wh);

                    HttpResponseMessage response = await client.SendAsync(requestMessage);

                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                        return Content(JsonSerializer.Serialize(new HmrcContent() { code = (int)response.StatusCode, message = response.ReasonPhrase }), JSON_FORMAT);
                    else
                        return Content(await response.Content.ReadAsStringAsync(), JSON_FORMAT);
                }
            }
            else
            {
                return Challenge(new AuthenticationProperties() { RedirectUri = vatIndexEndpoint }, "HMRC");
            }
        }

    }
}
