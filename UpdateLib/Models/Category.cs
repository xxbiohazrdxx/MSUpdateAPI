namespace UpdateLib.Models
{
	// This additional OwnedCategory class is needed as it is an owned type of the Update entity. Category cannot be used since it is 
	// already mapped to a container. 
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
