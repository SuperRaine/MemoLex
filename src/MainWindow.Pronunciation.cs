using System.IO;
using System.Windows;
using System.Windows.Media;
using MemoLex.Services;

namespace MemoLex;

public partial class MainWindow
{
    private readonly BaiduPronunciationClient _pronunciation = new();
    private CancellationTokenSource? _pronunciationRequest;
    private MediaPlayer? _pronunciationPlayer;
    private string? _pronunciationFile;
    private dynamic? _speaker;

    private async void SpeakButton_Click(object sender, RoutedEventArgs e)
    {
        var word = _word;
        if (_viewTarget != ViewTarget.Word || !ValidWord(word)) return;
        var accent = ReferenceEquals(sender, SpeakBritishButton) ? EnglishAccent.British : EnglishAccent.American;
        StopPronunciation();
        if (!_allowCloud)
        {
            SpeakSystemPronunciation(word, accent);
            return;
        }

        using var request = new CancellationTokenSource();
        _pronunciationRequest = request;
        StatusText.Text = "正在加载发音…";
        try
        {
            var audio = await _pronunciation.DownloadAsync(word, accent, request.Token);
            if (request.IsCancellationRequested) return;
            if (audio != null) PlayPronunciation(audio, word, accent);
            else SpeakSystemPronunciation(word, accent);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            ClearPronunciationPlayer();
            SpeakSystemPronunciation(word, accent);
        }
        finally
        {
            if (ReferenceEquals(_pronunciationRequest, request)) _pronunciationRequest = null;
        }
    }

    private void PlayPronunciation(byte[] audio, string word, EnglishAccent accent)
    {
        var file = Path.Combine(Path.GetTempPath(), $"MemoLex-{Guid.NewGuid():N}.mp3");
        using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
        {
            _pronunciationFile = file;
            stream.Write(audio);
        }
        var player = _pronunciationPlayer = new MediaPlayer();
        var playing = $"正在播放{AccentLabel(accent)}。";
        player.MediaOpened += (_, _) =>
        {
            if (ReferenceEquals(_pronunciationPlayer, player)) StatusText.Text = playing;
        };
        player.MediaEnded += (_, _) =>
        {
            if (!ReferenceEquals(_pronunciationPlayer, player)) return;
            ClearPronunciationPlayer();
            if (StatusText.Text == playing) StatusText.Text = $"{AccentLabel(accent)}播放完毕。";
        };
        player.MediaFailed += (_, _) =>
        {
            if (!ReferenceEquals(_pronunciationPlayer, player)) return;
            ClearPronunciationPlayer();
            SpeakSystemPronunciation(word, accent);
        };
        player.Open(new Uri(file));
        player.Play();
    }

    private void SpeakExample(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence)) return;
        StopPronunciation();
        SpeakSystemPronunciation(sentence, EnglishAccent.American, isSentence: true);
    }

    private void SpeakSystemPronunciation(string word, EnglishAccent accent, bool isSentence = false)
    {
        var label = isSentence ? "例句" : AccentLabel(accent);
        try
        {
            var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice") ?? throw new InvalidOperationException();
            _speaker ??= Activator.CreateInstance(voiceType);
            var language = accent == EnglishAccent.British ? "809" : "409";
            dynamic voices = _speaker!.GetVoices("Language=" + language);
            if (isSentence && voices.Count == 0) voices = _speaker.GetVoices("Language=809");
            if (voices.Count == 0) throw new InvalidOperationException();
            _speaker.Voice = voices.Item(0);
            _speaker.Rate = isSentence ? -1 : 0;
            // Speak plain text asynchronously and replace the previous utterance.
            _speaker.Speak(word, 19);
            StatusText.Text = $"正在播放{label}。";
        }
        catch { StatusText.Text = $"暂时无法播放{label}。"; }
    }

    private void StopPronunciation()
    {
        _pronunciationRequest?.Cancel();
        _pronunciationRequest = null;
        ClearPronunciationPlayer();
        if (_speaker != null)
        {
            try { _speaker.Speak(string.Empty, 3); }
            catch { _speaker = null; }
        }
    }

    private void ClearPronunciationPlayer()
    {
        var player = _pronunciationPlayer;
        _pronunciationPlayer = null;
        player?.Close();
        if (_pronunciationFile is not { } file) return;
        _pronunciationFile = null;
        try { File.Delete(file); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static string AccentLabel(EnglishAccent accent) => accent == EnglishAccent.British ? "英音" : "美音";
}
