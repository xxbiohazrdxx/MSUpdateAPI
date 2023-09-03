using System.Text.Json.Serialization;

namespace UpdateLib.Models
{
	public class Update
	{
		public Guid Id { get; set; } = default!;
		public string Title { get; set; } = default!;
		public string Description { get; set; } = default!;
		public DateTime CreationDate { get; set; } = default!;
		public string KBArticleId { get; set; } = default!;
		public List<OwnedProduct> Products { get; set; } = default!;
		public OwnedCategory? Classification { get; set; } = default!;
		public List<File> Files { get; set; } = default!;

		// EF Core for Cosmos only supports collections of primitives, which is why we ToString our GUIDs
		[JsonIgnore]
		public List<string> BundledUpdates { get; set; } = default!;
		[JsonIgnore]
		public List<string> SupersededUpdates { get; set; } = default!;


		public Update() { }
	}
}
