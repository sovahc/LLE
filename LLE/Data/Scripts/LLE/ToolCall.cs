using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using VRageMath;

namespace LLE
{
	class ToolCall
	{
		public readonly string Name;

		private readonly Dictionary<string, string> args;
		private readonly Dictionary<string, List<ToolCall>> lists;
		private string text;

		private ToolCall(string name, Dictionary<string, string> parsed,
			Dictionary<string, List<ToolCall>> parsedLists)
		{	Name = name;
			args = parsed;
			lists = parsedLists;
		}

		public string Text
		{
			get
			{
				if (text != null) return text;

				var sb = new StringBuilder(Name).Append('(');
				bool first = true;

				var tool = Tools.Find(Name);
				if (tool != null)
					foreach (var p in tool.Params)
					{
						if (p.Fields != null)
						{	List<ToolCall> entries;
							if (lists == null || !lists.TryGetValue(p.Name, out entries)) continue;
							if (!first) sb.Append(", ");
							first = false;
							sb.Append(p.Name).Append("=[");
							for (int e = 0; e < entries.Count; ++e)
							{	if (e != 0) sb.Append("; ");
								entries[e].AppendValues(sb, p.Fields);
							}
							sb.Append(']');
							continue;
						}

						string v;
						if (!args.TryGetValue(p.Name, out v)) continue;
						if (!first) sb.Append(", ");
						first = false;
						sb.Append(p.Name).Append('=').Append(v);
					}

				foreach (var kv in args)
				{	if (tool != null && HasParam(tool, kv.Key)) continue;
					if (!first) sb.Append(", ");
					first = false;
					sb.Append(kv.Key).Append('=').Append(kv.Value);
				}

				text = sb.Append(')').ToString();
				return text;
			}
		}

		private void AppendValues(StringBuilder sb, Tools.Param[] fields)
		{
			bool first = true;
			foreach (var f in fields)
			{	string v;
				if (!args.TryGetValue(f.Name, out v)) continue;
				if (!first) sb.Append(' ');
				first = false;
				sb.Append(v);
			}
		}

		public List<ToolCall> List(string key)
		{	List<ToolCall> entries;
			if (lists == null || !lists.TryGetValue(key, out entries)) return null;
			return entries;
		}

		private static bool HasParam(Tools.Tool tool, string name)
		{	foreach (var p in tool.Params)
				if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
			return false;
		}

		public override string ToString() => Text;

		public bool Is(string name) => Name == name;

		public bool Has(string key) => args.ContainsKey(key);

		public string Str(string key)
		{	string v;
			return args.TryGetValue(key, out v) ? v : "";
		}

		public bool Bool(string key)
		{	string v;
			if (!args.TryGetValue(key, out v)) return false;
			return v == "true" || v == "1";
		}

		public bool Int(string key, out int value)
		{	string v;
			if (!args.TryGetValue(key, out v)) { value = 0; return false; }
			// The model writes 5 where the schema says integer, but 5.0 happens; both are the cell 5.
			double d;
			if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)
				&& d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue)
			{	value = (int)d;
				return true;
			}
			value = 0;
			return false;
		}

		public bool Number(string key, out double value)
		{	string v;
			if (!args.TryGetValue(key, out v)) { value = 0; return false; }
			return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
		}

		public bool Ijk(out Vector3I value)
		{	return Triple("i", "j", "k", out value);
		}

		public bool Ijk2(out Vector3I value)
		{	return Triple("i2", "j2", "k2", out value);
		}

		public bool HasIjk => Has("i") || Has("j") || Has("k");

		private bool Triple(string ki, string kj, string kk, out Vector3I value)
		{	int i, j, k;
			if (Int(ki, out i) && Int(kj, out j) && Int(kk, out k))
			{	value = new Vector3I(i, j, k);
				return true;
			}
			value = Vector3I.Zero;
			return false;
		}

		public string NeedIjk => $"Error: {Name} needs the three integer arguments i, j, k.";
		public string NeedIjk2 => $"Error: {Name} needs the three integer arguments i2, j2, k2.";
		public string Need(string key) => $"Error: {Name} needs the argument {key}.";

		public static ToolCall Parse(string name, string argumentsJson, out string error)
		{
			error = null;

			if (string.IsNullOrEmpty(argumentsJson)) argumentsJson = "{}";

			var root = Json.Parse(argumentsJson, out error);
			if (root == null) return null;

			return FromObject(name, root, true, out error);
		}

		private static ToolCall FromObject(string name, Json root, bool allowLists, out string error)
		{
			error = null;

			if (!root.Is(JsonKind.Object))
			{	error = "the arguments are not an object";
				return null;
			}

			var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			Dictionary<string, List<ToolCall>> parsedLists = null;

			foreach (var field in root.Object)
			{
				var value = field.Value;

				if (value.Is(JsonKind.Null)) continue;

				if (value.Is(JsonKind.Array))
				{
					if (!allowLists)
					{	error = $"'{field.Key}' is a list inside a list";
						return null;
					}

					var entries = new List<ToolCall>();

					foreach (var element in value.Array)
					{
						if (!element.Is(JsonKind.Object))
						{	error = $"every entry of '{field.Key}' must be an object";
							return null;
						}

						var entry = FromObject(field.Key, element, false, out error);
						if (entry == null) return null;
						entries.Add(entry);
					}

					if (parsedLists == null)
						parsedLists = new Dictionary<string, List<ToolCall>>(StringComparer.OrdinalIgnoreCase);
					parsedLists[field.Key] = entries;
					continue;
				}

				if (value.Is(JsonKind.Object))
				{	error = $"'{field.Key}' is an object; every argument is a single value or a list";
					return null;
				}

				parsed[field.Key] = value.Is(JsonKind.String) ? value.String
					: value.Is(JsonKind.Bool) ? (value.Bool ? "true" : "false")
					: value.Raw;
			}

			return new ToolCall(name, parsed, parsedLists);
		}

	}
}
