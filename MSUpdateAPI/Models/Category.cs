namespace MSUpdateAPI.Models
{
	public class OwnedCategory
	{
		public Guid Id { get; set; }
		public string Name { get; set; }

		public OwnedCategory() { }
	}

	public class Category
	{
		public Guid Id { get; set; }
		public string Name { get; set; }

		public Category() { }
	}
}
