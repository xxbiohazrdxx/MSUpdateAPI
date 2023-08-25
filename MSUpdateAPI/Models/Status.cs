using System.Text.Json.Serialization;

namespace MSUpdateAPI.Models
{
	public class Status
	{
		public static readonly string LoadingMetadata = "Loading Metadata";
		public static readonly string Throttling = "Throttling";
		public static readonly string Idle = "Idle";

		public string State { get; set; }
		public int CategoryCount { get; set; }
		public int ProductCount { get; set; }
		[JsonIgnore]
		public int DetectoidCount { get; set; }
		public int UpdateCount { get; set; }
		public string[] RecentLogs { get; private set; } = new string[10];

		public Status() { }

		public void AddLogMessage(string Message)
		{
			Array.Copy(RecentLogs, 0, RecentLogs, 1, 9);
			RecentLogs[0] = Message;
		}
	}
}
