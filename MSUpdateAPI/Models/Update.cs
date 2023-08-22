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
		[JsonIgnore]
		public List<string> Superseded { get; set; }
		public List<File> Files { get; set; }

		public Update() { }
	}
}
