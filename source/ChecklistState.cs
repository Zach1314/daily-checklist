using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

[DataContract]
public sealed class ChecklistItem
{
    [DataMember] public string Text;
    [DataMember] public bool Done;
}

[DataContract]
public sealed class ChecklistState
{
    [DataMember] public string Day;
    [DataMember] public List<ChecklistItem> Items = new List<ChecklistItem>();
    [DataMember] public double? Left;
    [DataMember] public double? Top;
    [DataMember] public bool Pinned;
    [DataMember] public bool PositionLocked;
    [DataMember] public int Opacity = 78;

    public static string DayKey(DateTime date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public ChecklistState Copy()
    {
        var copy = (ChecklistState)MemberwiseClone();
        copy.Items = Items.Select(item => new ChecklistItem
        {
            Text = item.Text,
            Done = item.Done
        }).ToList();
        return copy;
    }

    public bool AdvanceDay(DateTime today)
    {
        string date = DayKey(today);
        if (Day == date)
            return false;

        Day = date;
        foreach (ChecklistItem item in Items)
            item.Done = false;
        return true;
    }

    public void ReplaceItems(string lines)
    {
        var previous = Items.GroupBy(item => item.Text, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new Queue<ChecklistItem>(group),
                StringComparer.Ordinal);
        var updated = new List<ChecklistItem>();
        using (var reader = new StringReader(lines))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string name = line.Trim();
                if (name.Length == 0)
                    continue;

                Queue<ChecklistItem> matching;
                bool done = previous.TryGetValue(name, out matching)
                    && matching.Count > 0 && matching.Dequeue().Done;
                updated.Add(new ChecklistItem { Text = name, Done = done });
            }
        }
        Items = updated;
    }
}

internal sealed class ChecklistStore
{
    private readonly string path;
    private readonly DataContractJsonSerializer serializer =
        new DataContractJsonSerializer(typeof(ChecklistState));

    public ChecklistStore(string path)
    {
        this.path = path;
    }

    public ChecklistState Read()
    {
        if (!File.Exists(path))
            return new ChecklistState();

        using (var stream = File.OpenRead(path))
        {
            var state = (ChecklistState)serializer.ReadObject(stream);
            if (state == null || state.Items == null || state.Items.Any(
                item => item == null || String.IsNullOrWhiteSpace(item.Text)))
                throw new SerializationException("The checklist file is invalid.");
            if (state.Opacity != 65 && state.Opacity != 78 && state.Opacity != 92)
                state.Opacity = 78;
            return state;
        }
    }

    public void Write(ChecklistState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
        {
            serializer.WriteObject(stream, state);
            stream.Flush(true);
        }
        if (File.Exists(path))
            File.Replace(temporary, path, null);
        else
            File.Move(temporary, path);
    }
}
