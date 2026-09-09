using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace CheckMailWM2.Models
{
    public class AccountItem : INotifyPropertyChanged
    {
        private int _id;
        private int _rowIndex;
        private string _rawData = string.Empty;
        private string _extractedEmail = string.Empty;
        private CheckStatus _status = CheckStatus.Pending;
        private string _note = string.Empty;
        private DateTime? _checkedTime;
        private int _targetStatusColumnIndex = -1;

        public int Id
        {
            get => _id;
            set => SetField(ref _id, value);
        }

        public int RowIndex
        {
            get => _rowIndex;
            set => SetField(ref _rowIndex, value);
        }

        public string RawData
        {
            get => _rawData;
            set => SetField(ref _rawData, value);
        }

        public string ExtractedEmail
        {
            get => _extractedEmail;
            set => SetField(ref _extractedEmail, value);
        }

        public CheckStatus Status
        {
            get => _status;
            set
            {
                if (SetField(ref _status, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusBackground));
                    OnPropertyChanged(nameof(StatusBorder));
                    OnPropertyChanged(nameof(StatusForeground));
                }
            }
        }

        public string StatusText => _status.ToDisplayString();
        public Brush StatusBackground => _status.GetBadgeBackground();
        public Brush StatusBorder => _status.GetBadgeBorder();
        public Brush StatusForeground => _status.GetBadgeForeground();

        public string Note
        {
            get => _note;
            set => SetField(ref _note, value);
        }

        public DateTime? CheckedTime
        {
            get => _checkedTime;
            set
            {
                if (SetField(ref _checkedTime, value))
                {
                    OnPropertyChanged(nameof(CheckedTimeString));
                }
            }
        }

        public string CheckedTimeString => _checkedTime?.ToString("HH:mm:ss") ?? "";

        public int TargetStatusColumnIndex
        {
            get => _targetStatusColumnIndex;
            set => SetField(ref _targetStatusColumnIndex, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
