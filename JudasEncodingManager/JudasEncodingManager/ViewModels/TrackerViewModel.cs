using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using JudasEncodingManager.Models;

namespace JudasEncodingManager.ViewModels
{
    public class TrackerViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private readonly TrackerConfig _model;
        public TrackerConfig Model => _model;

        public TrackerViewModel() : this(new TrackerConfig()) { }

        public TrackerViewModel(TrackerConfig model)
        {
            _model = model;
        }

        public string Id => _model.Id;

        public string Name
        {
            get => _model.Name;
            set { _model.Name = value; OnPropertyChanged(); }
        }

        public TrackerType Type
        {
            get => _model.Type;
            set { _model.Type = value; OnPropertyChanged(); }
        }

        public bool Enabled
        {
            get => _model.Enabled;
            set { _model.Enabled = value; OnPropertyChanged(); }
        }

        public string EndpointUrl
        {
            get => _model.EndpointUrl;
            set { _model.EndpointUrl = value; OnPropertyChanged(); }
        }

        public string Category
        {
            get => _model.Category;
            set { _model.Category = value; OnPropertyChanged(); }
        }

        public Dictionary<string, string> Credentials
        {
            get => _model.Credentials;
            set { _model.Credentials = value; OnPropertyChanged(); }
        }

        public string Notes
        {
            get => _model.Notes;
            set { _model.Notes = value; OnPropertyChanged(); }
        }

        // Helper to get/set credentials as a multi-line string
        public string CredentialsText
        {
            get => string.Join(Environment.NewLine, Credentials.Select(kv => $"{kv.Key}={kv.Value}"));
            set
            {
                var newCreds = new Dictionary<string, string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var lines = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            newCreds[parts[0].Trim()] = parts[1].Trim();
                        }
                    }
                }
                _model.Credentials = newCreds;
                OnPropertyChanged();
            }
        }

        public TrackerViewModel Clone()
        {
            var newModel = new TrackerConfig
            {
                Id = Guid.NewGuid().ToString(),
                Name = Name + " (Copy)",
                Type = Type,
                Enabled = false,
                EndpointUrl = EndpointUrl,
                Category = Category,
                Credentials = new Dictionary<string, string>(Credentials),
                Notes = Notes
            };
            return new TrackerViewModel(newModel);
        }

        // Create default trackers for common sites
        public static List<TrackerViewModel> GetDefaultTrackers()
        {
            return new List<TrackerViewModel>
            {
                new TrackerViewModel(new TrackerConfig
                {
                    Name = "TokyoTosho",
                    Type = TrackerType.TokyoTosho,
                    Enabled = false,
                    EndpointUrl = "https://www.tokyotosho.info/upload.php",
                    Category = "1", // Anime
                    Credentials = new Dictionary<string, string>
                    {
                        { "api_key", "" }
                    },
                    Notes = "Tokyo Tosho anime tracker"
                }),
                new TrackerViewModel(new TrackerConfig
                {
                    Name = "AnimeTosho",
                    Type = TrackerType.AnimeTosho,
                    Enabled = false,
                    EndpointUrl = "https://animetosho.org/",
                    Category = "",
                    Notes = "AnimeTosho automatically scrapes from Nyaa - no upload needed"
                })
            };
        }
    }
}
