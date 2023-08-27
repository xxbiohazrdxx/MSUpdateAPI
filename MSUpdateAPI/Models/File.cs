namespace MSUpdateAPI.Models
{
	public class File
	{
		public string FileName { get; set; }
		public string Source { get; set; }
		public DateTime ModifiedDate { get; set; }
		public FileDigest Digest { get; set; } 
		public ulong Size { get; set; }

		public File() { }
	}
}
