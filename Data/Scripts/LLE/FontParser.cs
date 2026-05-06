using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Utils;
using VRageMath;

namespace LLE
{
	public class FontParser
	{
		public struct Glyph
		{
			public Vector2 offset;
			public Vector2 size;
			public float aw;
			public int sx;
			public int sy;
		}
		public Dictionary<char, Glyph> Characters { get; } = new Dictionary<char, Glyph>();

		public bool Parse(string xmlPath)
		{
			try
			{
				using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInGameContent(xmlPath))
				{
					byte[] bytes = reader.ReadBytes((int)reader.BaseStream.Length);
					string content = System.Text.Encoding.UTF8.GetString(bytes);
					ParseXml(content);
				}
				return true;
			}
			catch (Exception e)
			{
				MyLog.Default.WriteLine("FontParser Error: " + e.Message);
				return false;
			}
		}

		private void ParseXml(string xml)
		{
			const int texSize = 1024;

			int idx = 0;
			while (true)
			{
				idx = FindTag(xml, "glyph", idx);
				if (idx == -1) break;
				string chStr = GetAttributeValue(xml, idx, "ch");
				char ch = ParseHtmlChar(chStr);

				string originVal = GetAttributeValue(xml, idx, "origin");
				string[] originParts = originVal.Split(',');
				int ox = int.Parse(originParts[0]);
				int oy = int.Parse(originParts[1]);

				string sizeVal = GetAttributeValue(xml, idx, "size");
				string[] sizeParts = sizeVal.Split('x');
				int sx = int.Parse(sizeParts[0]);
				int sy = int.Parse(sizeParts[1]);

				float aw = float.Parse(GetAttributeValue(xml, idx, "aw"));

				Glyph glyph = new Glyph();
				// Flip Y: SE textures use bottom-left origin, XNA fonts use top-left
				glyph.offset = new Vector2((float)ox / texSize, (float)(texSize - oy - sy) / texSize);
				glyph.size = new Vector2((float)sx / texSize, (float)sy / texSize);
				glyph.aw = aw;
				glyph.sx = sx;
				glyph.sy = sy;
				Characters[ch] = glyph;
			}
		}

		private static int FindTag(string xml, string tag, int startIndex)
		{
			string search = "<" + tag;
			int idx = xml.IndexOf(search, startIndex);
			while (idx != -1)
			{
				int after = idx + search.Length;
				if (after < xml.Length && (char.IsWhiteSpace(xml[after]) || xml[after] == '/' || xml[after] == '>'))
					return after;
				idx = xml.IndexOf(search, after);
			}
			return -1;
		}

		private static string GetAttributeValue(string xml, int startIndex, string attrName)
		{
			string search = " " + attrName + "=\"";
			int idx = xml.IndexOf(search, startIndex);
			if (idx == -1) return null;
			int valStart = idx + search.Length;
			int valEnd = xml.IndexOf('"', valStart);
			if (valEnd == -1) return null;
			return xml.Substring(valStart, valEnd - valStart);
		}

		private static char ParseHtmlChar(string text)
		{
			if (text.Length == 1) return text[0];
			if (text == "&quot;") return '"';
			if (text == "&amp;") return '&';
			if (text == "&gt;") return '>';
			if (text == "&lt;") return '<';
			throw new Exception("Unknown XML escaped character: " + text);
		}

	}
}
