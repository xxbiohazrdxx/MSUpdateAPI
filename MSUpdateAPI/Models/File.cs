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

		public File(string FileName, string Source, DateTime ModifiedDate, FileDigest Digest, ulong Size)
		{
			this.FileName = FileName;
			this.Source = Source;
			this.ModifiedDate = ModifiedDate;
			this.Digest = Digest;
			this.Size = Size;
		}
	}
}
