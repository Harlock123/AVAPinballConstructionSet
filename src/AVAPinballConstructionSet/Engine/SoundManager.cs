using AVAPinballConstructionSet.Audio;

namespace AVAPinballConstructionSet.Engine;

/// <summary>
/// Plays the program's sounds through OpenAL Soft, which mixes as many at once as a busy table needs. The tunes
/// play on the music channel. In a virtual machine a larger mixing buffer keeps the sound from breaking up.
/// </summary>
public sealed class SoundManager : IDisposable
{
    private readonly IAudioEngine _engine;
    private readonly string _folder;

    public SoundManager(bool silent = false)
    {
        _folder = Path.Combine(AppContext.BaseDirectory, "Sounds");
        _engine = silent
            ? new NullAudioEngine("silent")
            : OpenAlAudioEngine.CreateOrSilent(AudioBuffer.PeriodFrames(AudioBuffer.Automatic, VirtualMachine.Detect()));
        _engine.SetVolumes(0.9f, 0.8f, 0.7f);
        _engine.SetAmbienceVolume(0.45f);
    }

    public bool IsAvailable => _engine.IsAvailable;

    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (value) StopAll();
        }
    }
    private bool _muted;

    private string File(Sfx s) => Path.Combine(_folder, s + ".wav");

    public void Play(Sfx sfx)
    {
        if (_muted) return;
        switch (sfx)
        {
            case Sfx.TitleTune: _engine.PlayMusic(File(sfx), loop: true); return;
            case Sfx.GameOver or Sfx.HighScore: _engine.PlayMusic(File(sfx), loop: false); return;
            case Sfx.MusicStop: _engine.StopMusic(); return;
        }
        _engine.PlayEffect(File(sfx), sfx switch
        {
            Sfx.Thud => 0.35f, Sfx.Spinner => 0.5f, Sfx.Flipper => 0.6f, Sfx.Click or Sfx.Wire => 0.5f, Sfx.Rollover => 0.75f, Sfx.Pull => 0.6f,
            _ => 1f,
        });
    }

    public void StopAll()
    {
        _engine.StopAmbience();
        _engine.StopMusic();
    }

    public void Dispose() => _engine.Dispose();
}
