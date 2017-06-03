//
// ImageReader.cs
//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// (C) 2005 Jb Evain
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

namespace Mono.Cecil.Binary {

	using System;
	using System.IO;
	using System.Text;

	using Mono.Cecil.Metadata;

	class ImageReader : BaseImageVisitor {

		MetadataReader m_mdReader;
		BinaryReader m_binaryReader;
		Image m_image;

		public MetadataReader MetadataReader {
			get { return m_mdReader; }
		}

		public Image Image {
			get { return m_image; }
		}

		ImageReader (Image img, BinaryReader reader)
		{
			m_image = img;
			m_binaryReader = reader;
		}

		static ImageReader Read (Image img, Stream stream)
		{
			ImageReader reader = new ImageReader (img, new BinaryReader (stream));
			img.Accept (reader);
			return reader;
		}

		public static ImageReader Read (string file)
		{
			if (file == null)
				throw new ArgumentNullException ("file");

			FileInfo fi = new FileInfo (file);
			if (!File.Exists (fi.FullName))
			#if CF_1_0 || CF_2_0
				throw new FileNotFoundException (fi.FullName);
			#else
				throw new FileNotFoundException (string.Format ("File '{0}' not found.", fi.FullName), fi.FullName);
			#endif

			return Read (new Image (fi), new FileStream (
				fi.FullName, FileMode.Open,
				FileAccess.Read, FileShare.Read));
		}

		public static ImageReader Read (byte [] image)
		{
			if (image == null)
				throw new ArgumentNullException ("image");

			if (image.Length == 0)
				throw new ArgumentException ("Empty image array");

			return Read (new Image (), new MemoryStream (image));
		}

		public static ImageReader Read (Stream stream)
		{
			if (stream == null)
				throw new ArgumentNullException ("stream");

			if (!stream.CanRead)
				throw new ArgumentException ("Can not read from stream");

			return Read (new Image (), stream);
		}

		public BinaryReader GetReader ()
		{
			return m_binaryReader;
		}

		public override void VisitImage (Image img)
		{
			m_mdReader = new MetadataReader (this);
		}

		public override void VisitDOSHeader (DOSHeader header)
		{
			header.Start = m_binaryReader.ReadBytes (60);
			header.Lfanew = m_binaryReader.ReadUInt32 ();
			header.End = m_binaryReader.ReadBytes (64);

			m_binaryReader.BaseStream.Position = header.Lfanew;

			if (m_binaryReader.ReadUInt16 () != 0x4550 ||
				m_binaryReader.ReadUInt16 () != 0)

				throw new ImageFormatException ("Invalid PE File Signature");
		}

		public override void VisitPEFileHeader (PEFileHeader header)
		{
			header.Machine = m_binaryReader.ReadUInt16 ();
			header.NumberOfSections = m_binaryReader.ReadUInt16 ();
			header.TimeDateStamp = m_binaryReader.ReadUInt32 ();
			header.PointerToSymbolTable = m_binaryReader.ReadUInt32 ();
			header.NumberOfSymbols = m_binaryReader.ReadUInt32 ();
			header.OptionalHeaderSize = m_binaryReader.ReadUInt16 ();
			header.Characteristics = (Mono.Cecil.Binary.ImageCharacteristics) m_binaryReader.ReadUInt16 ();
		}

		ulong ReadIntOrLong ()
		{
			return m_image.PEOptionalHeader.StandardFields.IsPE64 ?
				m_binaryReader.ReadUInt64 () :
				m_binaryReader.ReadUInt32 ();
		}

		public override void VisitNTSpecificFieldsHeader (PEOptionalHeader.NTSpecificFieldsHeader header)
		{
			header.ImageBase = ReadIntOrLong ();
			header.SectionAlignment = m_binaryReader.ReadUInt32 ();
			header.FileAlignment = m_binaryReader.ReadUInt32 ();
			header.OSMajor = m_binaryReader.ReadUInt16 ();
			header.OSMinor = m_binaryReader.ReadUInt16 ();
			header.UserMajor = m_binaryReader.ReadUInt16 ();
			header.UserMinor = m_binaryReader.ReadUInt16 ();
			header.SubSysMajor = m_binaryReader.ReadUInt16 ();
			header.SubSysMinor = m_binaryReader.ReadUInt16 ();
			header.Reserved = m_binaryReader.ReadUInt32 ();
			header.ImageSize = m_binaryReader.ReadUInt32 ();
			header.HeaderSize = m_binaryReader.ReadUInt32 ();
			header.FileChecksum = m_binaryReader.ReadUInt32 ();
			header.SubSystem = (Mono.Cecil.Binary.SubSystem) m_binaryReader.ReadUInt16 ();
			header.DLLFlags = m_binaryReader.ReadUInt16 ();
			header.StackReserveSize = ReadIntOrLong ();
			header.StackCommitSize = ReadIntOrLong ();
			header.HeapReserveSize = ReadIntOrLong ();
			header.HeapCommitSize = ReadIntOrLong ();
			header.LoaderFlags = m_binaryReader.ReadUInt32 ();
			header.NumberOfDataDir = m_binaryReader.ReadUInt32 ();
		}

		public override void VisitStandardFieldsHeader (PEOptionalHeader.StandardFieldsHeader header)
		{
			header.Magic = m_binaryReader.ReadUInt16 ();
			header.LMajor = m_binaryReader.ReadByte ();
			header.LMinor = m_binaryReader.ReadByte ();
			header.CodeSize = m_binaryReader.ReadUInt32 ();
			header.InitializedDataSize = m_binaryReader.ReadUInt32 ();
			header.UninitializedDataSize = m_binaryReader.ReadUInt32 ();
			header.EntryPointRVA = new RVA (m_binaryReader.ReadUInt32 ());
			header.BaseOfCode = new RVA (m_binaryReader.ReadUInt32 ());
			if (!header.IsPE64)
				header.BaseOfData = new RVA (m_binaryReader.ReadUInt32 ());
		}

		public override void VisitDataDirectoriesHeader (PEOptionalHeader.DataDirectoriesHeader header)
		{
			header.ExportTable = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.ImportTable = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.ResourceTable = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.ExceptionTable = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.CertificateTable = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.BaseRelocationTable = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.Debug = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.Copyright = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.GlobalPtr = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.TLSTable = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.LoadConfigTable = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.BoundImport = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.IAT = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.DelayImportDescriptor = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.CLIHeader = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.Reserved = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
		}

		public override void VisitSectionCollection (SectionCollection coll)
		{
			for (int i = 0; i < m_image.PEFileHeader.NumberOfSections; i++)
				coll.Add (new Section ());
		}

		public override void VisitSection (Section sect)
		{
			char [] name, buffer = new char [8];
			int read = 0;
			while (read < 8) {
				char cur = (char) m_binaryReader.ReadSByte ();
				if (cur == '\0')
					break;
				buffer [read++] = cur;
			}
			name = new char [read];
			Array.Copy (buffer, 0, name, 0, read);
			sect.Name = read == 0 ? string.Empty : new string (name);
			if (sect.Name == Section.Text)
				m_image.TextSection = sect;
			m_binaryReader.BaseStream.Position += 8 - read - 1;
			sect.VirtualSize = m_binaryReader.ReadUInt32 ();
			sect.VirtualAddress = new RVA (m_binaryReader.ReadUInt32 ());
			sect.SizeOfRawData = m_binaryReader.ReadUInt32 ();
			sect.PointerToRawData = new RVA (m_binaryReader.ReadUInt32 ());
			sect.PointerToRelocations = new RVA (m_binaryReader.ReadUInt32 ());
			sect.PointerToLineNumbers = new RVA (m_binaryReader.ReadUInt32 ());
			sect.NumberOfRelocations = m_binaryReader.ReadUInt16 ();
			sect.NumberOfLineNumbers = m_binaryReader.ReadUInt16 ();
			sect.Characteristics = (Mono.Cecil.Binary.SectionCharacteristics) m_binaryReader.ReadUInt32 ();

			long pos = m_binaryReader.BaseStream.Position;
			m_binaryReader.BaseStream.Position = sect.PointerToRawData;
			sect.Data = m_binaryReader.ReadBytes ((int) sect.SizeOfRawData);
			m_binaryReader.BaseStream.Position = pos;
		}

		public override void VisitImportAddressTable (ImportAddressTable iat)
		{
			m_binaryReader.BaseStream.Position = m_image.ResolveVirtualAddress (
				m_image.PEOptionalHeader.DataDirectories.IAT.VirtualAddress);

			iat.HintNameTableRVA = new RVA (m_binaryReader.ReadUInt32 ());
		}

		public override void VisitCLIHeader (CLIHeader header)
		{
			if (m_image.PEOptionalHeader.DataDirectories.CLIHeader == DataDirectory.Zero)
				throw new ImageFormatException ("Non CLI Image");

			if (m_image.PEOptionalHeader.DataDirectories.Debug != DataDirectory.Zero) {
				m_image.DebugHeader = new DebugHeader ();
				VisitDebugHeader (m_image.DebugHeader);
			}

			m_binaryReader.BaseStream.Position = m_image.ResolveVirtualAddress (
				m_image.PEOptionalHeader.DataDirectories.CLIHeader.VirtualAddress);
			header.Cb = m_binaryReader.ReadUInt32 ();
			header.MajorRuntimeVersion = m_binaryReader.ReadUInt16 ();
			header.MinorRuntimeVersion = m_binaryReader.ReadUInt16 ();
			header.Metadata = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.Flags = (Mono.Cecil.Binary.RuntimeImage) m_binaryReader.ReadUInt32 ();
			header.EntryPointToken = m_binaryReader.ReadUInt32 ();
			header.Resources = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.StrongNameSignature = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.CodeManagerTable = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.VTableFixups = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.ExportAddressTableJumps = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());
			header.ManagedNativeHeader = new DataDirectory (
				new RVA (m_binaryReader.ReadUInt32 ()),
				m_binaryReader.ReadUInt32 ());

			if (header.StrongNameSignature != DataDirectory.Zero) {
				m_binaryReader.BaseStream.Position = m_image.ResolveVirtualAddress (
					header.StrongNameSignature.VirtualAddress);
				header.ImageHash = m_binaryReader.ReadBytes ((int) header.StrongNameSignature.Size);
			} else {
				header.ImageHash = new byte [0];
			}
			m_binaryReader.BaseStream.Position = m_image.ResolveVirtualAddress (
				m_image.CLIHeader.Metadata.VirtualAddress);
			m_image.MetadataRoot.Accept (m_mdReader);
		}

		public override void VisitDebugHeader (DebugHeader header)
		{
			if (m_image.PEOptionalHeader.DataDirectories.Debug == DataDirectory.Zero)
				return;

			long pos = m_binaryReader.BaseStream.Position;

			m_binaryReader.BaseStream.Position = m_image.ResolveVirtualAddress (
				m_image.PEOptionalHeader.DataDirectories.Debug.VirtualAddress);
			header.Characteristics = m_binaryReader.ReadUInt32 ();
			header.TimeDateStamp = m_binaryReader.ReadUInt32 ();
			header.MajorVersion = m_binaryReader.ReadUInt16 ();
			header.MinorVersion = m_binaryReader.ReadUInt16 ();
			header.Type = (Mono.Cecil.Binary.DebugStoreType) m_binaryReader.ReadUInt32 ();
			header.SizeOfData = m_binaryReader.ReadUInt32 ();
			header.AddressOfRawData = new RVA (m_binaryReader.ReadUInt32 ());
			header.PointerToRawData = m_binaryReader.ReadUInt32 ();

			m_binaryReader.BaseStream.Position = m_image.ResolveVirtualAddress (
				m_image.DebugHeader.AddressOfRawData);

			header.Magic = m_binaryReader.ReadUInt32 ();
			header.Signature = new Guid (m_binaryReader.ReadBytes (16));
			header.Age = m_binaryReader.ReadUInt32 ();

			StringBuilder buffer = new StringBuilder ();
			while (true) {
				byte cur =  m_binaryReader.ReadByte ();
				if (cur == 0)
					break;
				buffer.Append ((char) cur);
			}
			header.FileName = buffer.ToString ();

			m_binaryReader.BaseStream.Position = pos;
		}

		public override void VisitImportTable (ImportTable it)
		{
			m_binaryReader.BaseStream.Position = m_image.ResolveVirtualAddress (
				m_image.PEOptionalHeader.DataDirectories.ImportTable.VirtualAddress);

			it.ImportLookupTable = new RVA (m_binaryReader.ReadUInt32 ());
			it.DateTimeStamp = m_binaryReader.ReadUInt32 ();
			it.ForwardChain = m_binaryReader.ReadUInt32 ();
			it.Name = new RVA (m_binaryReader.ReadUInt32 ());
			it.ImportAddressTable = new RVA (m_binaryReader.ReadUInt32 ());
		}

		public override void VisitImportLookupTable (ImportLookupTable ilt)
		{
			m_binaryReader.BaseStream.Position = m_image.ResolveVirtualAddress (
				m_image.ImportTable.ImportLookupTable.Value);

			ilt.HintNameRVA = new RVA (m_binaryReader.ReadUInt32 ());
		}

		public override void VisitHintNameTable (HintNameTable hnt)
		{
			m_binaryReader.BaseStream.Position = m_image.ResolveVirtualAddress (
				m_image.ImportAddressTable.HintNameTableRVA);

			hnt.Hint = m_binaryReader.ReadUInt16 ();

			byte [] bytes = m_binaryReader.ReadBytes (11);
			hnt.RuntimeMain = Encoding.ASCII.GetString (bytes, 0, bytes.Length);

			m_binaryReader.BaseStream.Position = m_image.ResolveVirtualAddress (
				m_image.ImportTable.Name);

			bytes = m_binaryReader.ReadBytes (11);
			hnt.RuntimeLibrary = Encoding.ASCII.GetString (bytes, 0, bytes.Length);

			m_binaryReader.BaseStream.Position = m_image.ResolveVirtualAddress (
				m_image.PEOptionalHeader.StandardFields.EntryPointRVA);
			hnt.EntryPoint = m_binaryReader.ReadUInt16 ();
			hnt.RVA = new RVA (m_binaryReader.ReadUInt32 ());
		}

		public override void TerminateImage (Image img)
		{
			m_binaryReader.Close ();

			try {
				ResourceReader resReader = new ResourceReader (img);
				img.ResourceDirectoryRoot = resReader.Read ();
			} catch {
				img.ResourceDirectoryRoot = null;
			}
		}
	}
}
