using System.IO;
using System.Windows;
using System.Windows.Media;
using MemoLex.Services;

namespace MemoLex;

public partial class MainWindow
{
    private readonly PronunciationClient _pronunciation = new();
    private CancellationTokenSource? _pronunciationRequest;
    private MediaPlayer? _pronunciationPlayer;
    private string? _pronunciationFile;

    private async void SpeakButton_Click(object sender, RoutedEventArgs e)
    {
        var word = _word;
        if (_viewTarget != ViewTarget.Word || !ValidWord(word)) return;
        var accent = ReferenceEquals(sender, SpeakBritishButton) ? EnglishAccent.British : EnglishAccent.American;
        await PlaySpeechAsync(word, accent);
    }

    private Task SpeakExampleAsync(string sentence) => string.IsNullOrWhiteSpace(sentence)
        ? Task.CompletedTask : PlaySpeechAsync(sentence, EnglishAccent.American, isSentence: true);

    private async Task PlaySpeechAsync(string text, EnglishAccent accent, bool isSentence = false)
    {
        StopPronunciation();
        if (!_allowCloud)
        {
            StatusText.Text = "离线时无法播放语音。";
            return;
        }

        var label = isSentence ? "例句" : AccentLabel(accent);
        using var request = new CancellationTokenSource();
        _pronunciationRequest = request;
        StatusText.Text = isSentence ? "正在加载例句语音…" : "正在加载发音…";
        try
        {
            var audio = isSentence
                ? await _pronunciation.DownloadSentenceAsync(text, request.Token)
                : await _pronunciation.DownloadAsync(text, accent, request.Token);
            if (request.IsCancellationRequested) return;
            if (audio != null) PlayPronunciation(audio, label);
            else StatusText.Text = "在线语音暂不可用，请稍后重试。";
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            if (request.IsCancellationRequested) return;
            ClearPronunciationPlayer();
            StatusText.Text = "未能播放语音，请稍后重试。";
        }
        finally
        {
            if (ReferenceEquals(_pronunciationRequest, request)) _pronunciationRequest = null;
        }
    }

    private void PlayPronunciation(byte[] audio, string label)
    {
        var file = Path.Combine(Path.GetTempPath(), $"MemoLex-{Guid.NewGuid():N}.mp3");
        using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
        {
            _pronunciationFile = file;
            stream.Write(audio);
        }
        var player = _pronunciationPlayer = new MediaPlayer();
        var playing = $"正在播放{label}。";
        player.MediaOpened += (_, _) =>
        {
            if (ReferenceEquals(_pronunciationPlayer, player)) StatusText.Text = playing;
        };
        player.MediaEnded += (_, _) =>
        {
            if (!ReferenceEquals(_pronunciationPlayer, player)) return;
            ClearPronunciationPlayer();
            if (StatusText.Text == playing) StatusText.Text = $"{label}播放完毕。";
        };
        player.MediaFailed += (_, _) =>
        {
            if (!ReferenceEquals(_pronunciationPlayer, player)) return;
            ClearPronunciationPlayer();
            StatusText.Text = "未能播放语音，请稍后重试。";
        };
        player.Open(new Uri(file));
        player.Play();
    }

    private void StopPronunciation()
    {
        _pronunciationRequest?.Cancel();
        _pronunciationRequest = null;
        ClearPronunciationPlayer();
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
