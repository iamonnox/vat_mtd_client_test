using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Controllers
{
    public class VatMTDController : Controller
    {
        const string api = "vat mtd (v1.0)";
        const string vatObligationsEndpoint = "/vat/obligations";
        const string vatViewReturnEndpoint = "/vat/returns";
        const string vatLiabilitiesEndpoint = "/vat/liabilities";
        const string vatReturnsEndpoint = "/vat/returns";

        //Test Id: 489940671012
        //Pwd: Ksu1opfpmapi
        const string VAT_NUMBER = "383187528";
        const string PERIOD_KEY = "18A1";

        const string JSON_FORMAT = "application/json";

        private readonly ClientSettings _clientSettings;

        public VatMTDController(IOptions<ClientSettings> clientSettingsOptions)
        {
            _clientSettings = clientSettingsOptions.Value;
        }

        public IActionResult Index()
        {
            ViewData["service"] = api;
            ViewData["vatObligationsEndpoint"] = vatObligationsEndpoint;
            ViewData["vatViewReturnEndpoint"] = vatViewReturnEndpoint;
            ViewData["vatLiabilitiesEndpoint"] = vatLiabilitiesEndpoint;
            ViewData["vatReturnsEndpoint"] = vatReturnsEndpoint;

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

        public IActionResult ChallangeCall()
        {
            return Challenge(new AuthenticationProperties() { RedirectUri = "/VatMTD/VatObligationsCall" }, "HMRC");
        }

        public async Task<IActionResult> VatObligationsCall()
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

                    HttpResponseMessage response = await client.GetAsync($"/organisations/vat/{VAT_NUMBER}/obligations?status=O"); //?status=O || F

                    String resp = await response.Content.ReadAsStringAsync();
                    return Content(resp, JSON_FORMAT);
                }
            }
            else
            {
                return Challenge(new AuthenticationProperties() { RedirectUri = "/VatMTD/VatObligationsCall" }, "HMRC");
            }
        }

        public async Task<IActionResult> VatViewReturnCall()
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

                    HttpResponseMessage response = await client.GetAsync($"/organisations/vat/{VAT_NUMBER}/returns/{PERIOD_KEY}");

                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                        return Content(JsonSerializer.Serialize(new HmrcContent() { code = (int)response.StatusCode, message = response.ReasonPhrase }), JSON_FORMAT);
                    else
                        return Content(await response.Content.ReadAsStringAsync(), JSON_FORMAT);
                }
            }
            else
            {
                return Challenge(new AuthenticationProperties() { RedirectUri = "/VatMTD/VatViewReturnCall" }, "HMRC");
            }
        }

        public async Task<IActionResult> VatLiabilitiesCall()
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

                    HttpResponseMessage response = await client.GetAsync($"/organisations/vat/{VAT_NUMBER}/liabilities?from=2017-01-01&to=2017-03-31");

                    String resp = await response.Content.ReadAsStringAsync();
                    return Content(resp, JSON_FORMAT);
                }
            }
            else
            {
                return Challenge(new AuthenticationProperties() { RedirectUri = "/VatMTD/VatLiabilitiesCall" }, "HMRC");
            }
        }

        public async Task<IActionResult> VatReturnCall()
        {

            VatReturn vatReturn = new()
            {
                periodKey = PERIOD_KEY,
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
                        RequestUri = new Uri($"{_clientSettings.Uri}/organisations/vat/{VAT_NUMBER}/returns"),
                        Method = HttpMethod.Post
                    };

                    requestMessage.Content.Headers.ContentType = new MediaTypeWithQualityHeaderValue(JSON_FORMAT);

                    HttpResponseMessage response = await client.SendAsync(requestMessage);

                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                        return Content(JsonSerializer.Serialize(new HmrcContent() { code = (int)response.StatusCode, message = response.ReasonPhrase }), JSON_FORMAT);
                    else
                        return Content(await response.Content.ReadAsStringAsync(), JSON_FORMAT);
                }
            }
            else
            {
                return Challenge(new AuthenticationProperties() { RedirectUri = "/VatMTD/VatReturnCall" }, "HMRC");
            }
        }

    }
}
