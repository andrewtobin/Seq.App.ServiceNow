using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Seq.Apps;
using Seq.Apps.LogEvents;

namespace Seq.App.ServiceNow
{
    [SeqApp("ServiceNow", Description = "Creates a ServiceNow Incident for a Seq event.")]
    public class ServiceNowReactor: Reactor, ISubscribeTo<LogEventData>
    {
        readonly ConcurrentDictionary<uint, DateTime> _lastSeen = new ConcurrentDictionary<uint, DateTime>();

        [SeqAppSetting(
            DisplayName = "Instance",
            HelpText = "The name of your ServiceNow instance (eg \"instance\" = https://instance.service-now.com/).")]
        public string Instance { get; set; }

        [SeqAppSetting(
            DisplayName = "Username",
            HelpText = "The account with REST API access to ServiceNow.")]
        public string Username { get; set; }

        [SeqAppSetting(
            DisplayName = "Password",
            InputType = SettingInputType.Password,
            HelpText = "The password for the account.")]
        public string Password { get; set; }

        [SeqAppSetting(
            DisplayName = "Assign To",
            HelpText = "Person to assign the Incident to.",
            IsOptional = true)]
        public string AssignTo { get; set; }

        [SeqAppSetting(
        DisplayName = "Suppression time (minutes)",
        IsOptional = true,
        HelpText = "Once an event type has been sent, the time to wait before sending again. The default is zero.")]
        public int SuppressionMinutes { get; set; }

        public void On(Event<LogEventData> evt)
        {
            bool added = false;
            var lastSeen = _lastSeen.GetOrAdd(evt.EventType, k => { added = true; return DateTime.UtcNow; });
            if (!added)
            {
                if (lastSeen > DateTime.UtcNow.AddMinutes(-SuppressionMinutes)) return;
                _lastSeen[evt.EventType] = DateTime.UtcNow;
            }

            var message = new StringBuilder();

            foreach (var property in evt.Data.Properties)
                message.AppendLine($"{property.Key}: {property.Value}");

            message.AppendLine();
            message.AppendLine("Exception:");
            message.AppendLine(evt.Data.Exception);

            var record = new
            {
                @assigned_to = AssignTo,
                @short_description = "[" + evt.Data.Level + "] " + evt.Data.RenderedMessage,
                @description = message,
            };


            var credentials = new CredentialCache { { new Uri($"https://{Instance}.service-now.com/"), "Basic", new NetworkCredential(Username, Password) }};

            using (var client = new HttpClient(new HttpClientHandler { Credentials = credentials }))
            {
                var response = client.PostAsync($"https://{Instance}.service-now.com/api/now/v1/table/incident/", 
                    new StringContent(JsonConvert.SerializeObject(record), Encoding.UTF8, "application/json")).Result;

                response.EnsureSuccessStatusCode();
            }
        }
    }
}
