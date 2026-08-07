using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using VRageMath;

namespace LLE
{
	// One call the model issued: a tool name and its arguments. llama.cpp has already turned
	// Gemma's own call syntax into a name and a flat JSON object, so all that is left here is
	// reading that object — values are kept as text and converted where they are used.
	//
	// The arguments are always flat: a string, a number or a boolean per key. Nothing in Tools
	// declares an object or an array, so a nested value means the model invented one, and that is
	// reported rather than guessed at.
	class ToolCall
	{
		public readonly string Name;

		private readonly Dictionary<string, string> args;
		private string text;

		private ToolCall(string name, Dictionary<string, string> parsed)
		{	Name = name;
			args = parsed;
		}

		// Canonical form: the console, the transcript, the loop detector and the vote all compare
		// and print calls through this. Argument order comes from the schema, never from the model
		// — two streams that issued the same call with the keys in a different order are the same
		// plan and must read as one.
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
					{	string v;
						if (!args.TryGetValue(p.Name, out v)) continue;
						if (!first) sb.Append(", ");
						first = false;
						sb.Append(p.Name).Append('=').Append(v);
					}

				// Arguments the schema never declared still belong in the text: a call that carries
				// one is not the same call as one that does not, and the vote must see the difference.
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

		private static bool HasParam(Tools.Tool tool, string name)
		{	foreach (var p in tool.Params)
				if (p.Name == name) return true;
			return false;
		}

		public override string ToString() => Text;

		public bool Is(string name) => Name == name;

		public bool Has(string key) => args.ContainsKey(key);

		// Absent reads as empty: every command checks what it needs and answers with its own words.
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

			// A call with no arguments sends nothing at all; the request still needs a value there.
			if (string.IsNullOrEmpty(argumentsJson)) argumentsJson = "{}";

			var root = Json.Parse(argumentsJson, out error);
			if (root == null) return null;

			if (!root.Is(JsonKind.Object))
			{	error = "the arguments are not an object";
				return null;
			}

			var parsed = new Dictionary<string, string>();

			foreach (var field in root.Object)
			{
				var value = field.Value;

				if (value.Is(JsonKind.Array) || value.Is(JsonKind.Object))
				{	error = $"'{field.Key}' is a list or an object; every argument is a single value";
					return null;
				}

				// A string reads as its text, everything else as it was written: the model wrote 5
				// and the console should not show 5.0 back at it.
				parsed[field.Key] = value.Is(JsonKind.String) ? value.String : value.Raw;
			}

			return new ToolCall(name, parsed);
		}

	}
}
