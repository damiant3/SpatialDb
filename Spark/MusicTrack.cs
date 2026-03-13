using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
///////////////////////////////////////////////
namespace Spark;

/// <summary>
/// Metadata for a generated music track — the audio equivalent of <see cref="ImageRecord"/>.
/// </summary>
sealed class MusicTrack : INotifyPropertyChanged
{
    [JsonPropertyName("id")]           public int Id { get; set; }
    [JsonPropertyName("title")]        public string Title { get; set; } = "";
    [JsonPropertyName("prompt")]       public string Prompt { get; set; } = "";
    [JsonPropertyName("filePath")]     public string FilePath { get; set; } = "";
    [JsonPropertyName("duration")]     public double Duration { get; set; }
    [JsonPropertyName("temperature")]  public double Temperature { get; set; } = 1.0;
    [JsonPropertyName("cfgCoeff")]     public double CfgCoefficient { get; set; } = 3.0;
    [JsonPropertyName("createdUtc")]   public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("vibeTag")]      public string VibeTag { get; set; } = "";
    [JsonPropertyName("bpm")]          public float Bpm { get; set; }

    int m_rating;
    [JsonPropertyName("rating")]
    public int Rating { get => m_rating; set { m_rating = value; OnPropertyChanged(); OnPropertyChanged(nameof(RatingStars)); } }

    bool m_saved;
    [JsonPropertyName("saved")]
    public bool Saved { get => m_saved; set { m_saved = value; OnPropertyChanged(); } }

    bool m_deleted;
    [JsonPropertyName("deleted")]
    public bool Deleted { get => m_deleted; set { m_deleted = value; OnPropertyChanged(); } }

    [JsonIgnore] public string DisplayName => $"#{Id:D2} — {Title}";
    [JsonIgnore] public string FileName => Path.GetFileName(FilePath);
    [JsonIgnore] public string RatingStars => Rating > 0 ? new string('★', Rating) + new string('☆', 5 - Rating) : "";
    [JsonIgnore] public string DurationLabel => TimeSpan.FromSeconds(Duration).ToString(@"m\:ss");
    [JsonIgnore] public bool Exists => File.Exists(FilePath);

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
