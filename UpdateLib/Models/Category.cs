using System.Text.Json.Serialization;

namespace UpdateLib.Models
{
	// This additional OwnedCategory class is needed as it is an owned type of the Update entity. Category cannot be used since it is 
	// already mapped to a container. 
	public class OwnedCategory
	{
		public Guid Id { get; set; } = default!;
		public string Name { get; set; } = default!;

		public OwnedCategory() { }
	}

	public class Category
	{
		public Guid Id { get; set; } = default!;
		public string Name { get; set; } = default!;
		[JsonIgnore]
		public bool Enabled { get; set; } = default!;

		public Category() { }
	}
}
