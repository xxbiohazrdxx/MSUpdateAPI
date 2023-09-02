using System.Text.Json.Serialization;

namespace UpdateLib.Models
{
	public class Status
	{
		public static readonly string LoadingMetadata = "Loading Metadata";
		public static readonly string Idle = "Idle";

		public int Id { get; set; }
		public bool InitialSyncComplete { get; set; }
		public string State { get; set; }

		public Status() { }
	}
}
