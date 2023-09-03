using System.Text.Json.Serialization;

namespace UpdateLib.Models
{
	public class Status
	{
		public static readonly string LoadingMetadata = "Loading Metadata";
		public static readonly string Idle = "Idle";

		[JsonIgnore]
		public string Id { get; set; } = default!;
		public bool InitialSyncComplete { get; set; } = default!;
		public string State { get; set; } = default!;

		public Status() { }
	}
}
