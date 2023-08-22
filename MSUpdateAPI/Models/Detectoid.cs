namespace MSUpdateAPI.Models
{
	public class Detectoid
	{
		public Guid Id { get; set; }
		public string Name { get; set; }

		public Detectoid() { }

		public Detectoid(Guid Id, string Name)
		{
			this.Id = Id;
			this.Name = Name;
		}
	}
}
