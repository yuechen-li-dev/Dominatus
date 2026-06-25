using System.Text.Json;
using Dominatus.Actuators.Audio;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Runtime;

public sealed class AudioTests
{
    [Fact] public void AudioProviderRegistry_RejectsDuplicateProviders() { var r = new AudioProviderRegistry().Register(new FakeAudioProvider("fake")); Assert.Throws<InvalidOperationException>(() => r.Register(new FakeAudioProvider("FAKE"))); }
    [Fact] public void AudioProviderRegistry_GetsProviderCaseInsensitive() { var p = new FakeAudioProvider("fake"); Assert.Same(p, new AudioProviderRegistry().Register(p).GetRequired("FAKE")); }
    [Fact] public void TextToSpeechCommand_RequiresProvider() => Assert.Throws<ArgumentException>(() => AudioValidation.Validate(Tts() with { ProviderId = "" }));
    [Fact] public void TextToSpeechCommand_RequiresIdempotencyKey() => Assert.Throws<ArgumentException>(() => AudioValidation.Validate(Tts() with { IdempotencyKey = "" }));
    [Fact] public void TextToSpeechCommand_RequiresText() => Assert.Throws<ArgumentException>(() => AudioValidation.Validate(Tts() with { Text = "" }));
    [Fact] public void TextToSpeechCommand_RequiresOutputPath() => Assert.Throws<ArgumentException>(() => AudioValidation.Validate(Tts() with { OutputPath = "" }));
    [Fact] public void MetadataPolicy_DefaultWritesSidecarButNotInputText() { var p = new AudioMetadataPolicy(); Assert.True(p.WriteSidecarJson); Assert.False(p.IncludeInputTextInMetadata); Assert.True(p.IncludeTextHash); Assert.False(p.EmbedOpenFileMetadata); }
    [Fact] public async Task FakeAudioProvider_TextToSpeech_WritesValidWav() { var res = await Provider().TextToSpeechAsync(Tts(), default); AssertWav(res.Artifact.Path); }
    [Fact] public async Task FakeAudioProvider_TextToSpeech_WritesSidecarMetadata() { var res = await Provider().TextToSpeechAsync(Tts(), default); Assert.True(File.Exists(res.Artifact.MetadataPath)); }
    [Fact] public async Task FakeAudioProvider_TextToSpeech_MetadataIncludesTextHashButNotTextByDefault() { var res = await Provider().TextToSpeechAsync(Tts() with { Text = "secret words" }, default); var json = File.ReadAllText(res.Artifact.MetadataPath!); Assert.Contains("textSha256", json); Assert.DoesNotContain("secret words", json); }
    [Fact] public async Task FakeAudioProvider_TextToSpeech_CanIncludeInputTextWhenPolicyAllows() { var res = await Provider().TextToSpeechAsync(Tts() with { Text = "include me", MetadataPolicy = new() { IncludeInputTextInMetadata = true } }, default); Assert.Contains("include me", File.ReadAllText(res.Artifact.MetadataPath!)); }
    [Fact] public async Task FakeAudioProvider_SoundEffect_WritesValidWav() { var res = await Provider().GenerateSoundEffectAsync(Sfx(), default); AssertWav(res.Artifact.Path); }
    [Fact] public async Task FakeAudioProvider_Idempotency_RepeatedSameKeyReturnsSameResult() { var p = Provider(); var c = Tts(); var a = await p.TextToSpeechAsync(c, default); var b = await p.TextToSpeechAsync(c, default); Assert.Equal(a.Artifact.Path, b.Artifact.Path); Assert.True(b.FromCache); }
    [Fact] public async Task FakeAudioProvider_Idempotency_ConflictingPayloadFails() { var p = Provider(); await p.TextToSpeechAsync(Tts(), default); await Assert.ThrowsAsync<InvalidOperationException>(async () => await p.TextToSpeechAsync(Tts() with { Text = "different" }, default)); }
    [Fact] public void AudioActuationHandler_DispatchesTextToSpeech() { var (host, ctx) = Ctx(); var res = host.Dispatch(ctx, Tts()); Assert.True(res.Ok); Assert.IsType<TextToSpeechResult>(res.Payload); }
    [Fact] public void AudioActuationHandler_DispatchesSoundEffect() { var (host, ctx) = Ctx(); var res = host.Dispatch(ctx, Sfx()); Assert.True(res.Ok); Assert.IsType<GenerateSoundEffectResult>(res.Payload); }
    [Fact] public void AudioActuationHandler_UnknownProviderFailsSafely() { var (host, ctx) = Ctx(); var res = host.Dispatch(ctx, Tts() with { ProviderId = "missing" }); Assert.False(res.Ok); Assert.Contains("Unknown audio provider", res.Error); }
    [Fact] public void AudioErrors_DoNotLeakSecrets() { var error = AudioActuationHandler.Sanitize(new InvalidOperationException("bad sk-secret api_key token")); Assert.DoesNotContain("sk-secret", error); Assert.DoesNotContain("api_key", error); }
    [Fact] public async Task NoHiddenFingerprinting_MetadataStatesNoDominatusHiddenWatermark() { var res = await Provider().TextToSpeechAsync(Tts(), default); using var doc = JsonDocument.Parse(File.ReadAllText(res.Artifact.MetadataPath!)); Assert.False(doc.RootElement.GetProperty("hiddenWatermarkAddedByDominatus").GetBoolean()); }
    [Fact] public void Package_HasNoElevenLabsOpenAiQwenProviderDependenciesInM0() { var csproj = File.ReadAllText(Path.Combine(Root(), "src/Dominatus.Actuators.Audio/Dominatus.Actuators.Audio.csproj")); Assert.DoesNotContain("Eleven", csproj); Assert.DoesNotContain("OpenAI", csproj); Assert.DoesNotContain("Qwen", csproj); }
    private static FakeAudioProvider Provider() => new();
    private static string Tmp(string name) => Path.Combine(Path.GetTempPath(), "dominatus-audio-tests", Guid.NewGuid().ToString("N"), name);
    private static TextToSpeechCommand Tts() => new() { ProviderId = "fake", IdempotencyKey = "key", Text = "hello", OutputPath = Tmp("out.wav") };
    private static GenerateSoundEffectCommand Sfx() => new() { ProviderId = "fake", IdempotencyKey = "sfx", Prompt = "zap", OutputPath = Tmp("sfx.wav"), DurationSeconds = .25 };
    private static void AssertWav(string path) { var b = File.ReadAllBytes(path); Assert.Equal((byte)'R', b[0]); Assert.Equal((byte)'I', b[1]); Assert.Equal((byte)'F', b[2]); Assert.Equal((byte)'F', b[3]); Assert.Equal((byte)'W', b[8]); Assert.Equal((byte)'A', b[9]); Assert.Equal((byte)'V', b[10]); Assert.Equal((byte)'E', b[11]); }
    private static (ActuatorHost, AiCtx) Ctx() { var host = new ActuatorHost().RegisterAudioActuators(new AudioProviderRegistry().Register(new FakeAudioProvider())); var world = new AiWorld(host); var agent = new AiAgent(new HfsmInstance(new HfsmGraph { Root = "root" })); world.Add(agent); return (host, new AiCtx(world, agent, agent.Events, default, world.View, world.Mail, host, new LiveWorldBb(world.Bb))); }
    private static string Root() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "Dominatus.slnx"))) d = d.Parent; return d!.FullName; }
}
