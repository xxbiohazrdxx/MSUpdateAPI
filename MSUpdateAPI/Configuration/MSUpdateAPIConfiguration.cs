namespace MSUpdateAPI.Configuration
{
	public class MSUpdateAPIConfiguration
	{
		public int RefreshIntervalHours { get; set; } = 12;
		public int RefreshIntervalMinutes { get; set; } = 0;
		public List<Guid> EnabledCategories { get; set; } = new List<Guid>();
		public List<Guid> EnabledProducts { get; set; } = new List<Guid>();
	}
}
