using System.Text.Json.Serialization;

namespace MSUpdateAPI.Models
{
	public class Update
	{
		public Guid Id { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public DateTime CreationDate { get; set; }
		public string KBArticleId { get; set; }
		public List<OwnedProduct> Products { get; set; }
		public OwnedCategory? Classification { get; set; }
		public List<File> Files { get; set; }

		// EF Core for Cosmos only supports collections of primitives, which is why we ToString our GUIDs
		[JsonIgnore]
		public List<string> BundledUpdates { get; set; }
		[JsonIgnore]
		public List<string> SupersededUpdates { get; set; }


		public Update() { }
	}
}
