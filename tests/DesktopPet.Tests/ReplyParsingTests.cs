using DesktopPet.Engine;
using DesktopPet.Services.Ai;
using Xunit;

namespace DesktopPet.Tests;

/// <summary>
/// Parsing what the model says. Models dress their output up — backticks, bold, stray
/// punctuation — and anything the parser misses gets read aloud by the cat.
/// </summary>
public class ReplyParsingTests
{
    [Fact]
    public void A_plain_reply_keeps_its_text_and_emotion()
    {
        var (text, tag) = AiChatService.ParseReply("Hello there! [joy]");
        Assert.Equal("Hello there!", text);
        Assert.Equal("joy", tag);
    }

    [Fact]
    public void A_missing_tag_falls_back_to_neutral()
    {
        var (text, tag) = AiChatService.ParseReply("Just a cat noise.");
        Assert.Equal("Just a cat noise.", text);
        Assert.Equal("neutral", tag);
    }

    [Fact]
    public void A_tag_followed_by_a_stray_full_stop_still_parses()
    {
        var (text, tag) = AiChatService.ParseReply("Nap time [sleepy].");
        Assert.Equal("Nap time", text);
        Assert.Equal("sleepy", tag);
    }

    [Fact]
    public void Remember_notes_are_hidden_from_the_user()
    {
        var (text, _) = AiChatService.ParseReply("Nice to meet you <<remember: name is Jordan>> [joy]");
        Assert.DoesNotContain("remember", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Jordan", text);
    }

    [Theory]
    [InlineData("<<tool:get_time>>")]
    [InlineData("`<<tool:get_time>>`")]          // wrapped in code formatting
    [InlineData("**<<tool:get_time>>**")]        // bolded
    [InlineData("<< tool : get_time >>")]        // spaced out
    public void Tool_tokens_are_stripped_however_the_model_dresses_them_up(string raw)
    {
        var (text, _) = AiChatService.ParseReply(raw);
        Assert.DoesNotContain("tool", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("get_time", text);
    }

    [Fact]
    public void An_empty_reply_becomes_a_cat_noise_rather_than_a_blank_bubble()
    {
        Assert.Equal("*mrrp*", AiChatService.ParseReply("").text);
        Assert.Equal("*mrrp*", AiChatService.ParseReply("[joy]").text);
    }

    [Fact]
    public void Null_input_does_not_throw()
    {
        var (text, tag) = AiChatService.ParseReply(null!);
        Assert.Equal("*mrrp*", text);
        Assert.Equal("neutral", tag);
    }

    [Fact]
    public void Every_tag_offered_to_the_model_maps_to_a_real_state()
    {
        // If the prompt advertises a tag the map doesn't know, the cat silently falls back to
        // Talk and the emotion feature quietly half-works.
        foreach (string tag in EmotionMap.AllowedTags)
        {
            var state = EmotionMap.Resolve(tag);
            Assert.True(Enum.IsDefined(typeof(PetState), state), $"'{tag}' resolved to {state}");
        }
    }

    [Fact]
    public void Emotions_never_map_to_a_state_the_cat_cannot_leave_on_its_own()
    {
        // Pet and Hunt need external input to exit; a chat reply landing there strands the cat.
        foreach (string tag in EmotionMap.AllowedTags)
        {
            var state = EmotionMap.Resolve(tag);
            Assert.NotEqual(PetState.Pet, state);
            Assert.NotEqual(PetState.Drag, state);
        }
    }

    [Fact]
    public void An_unknown_tag_resolves_to_a_harmless_default()
    {
        Assert.Equal(PetState.Talk, EmotionMap.Resolve("ecstatic"));
        Assert.Equal(PetState.Talk, EmotionMap.Resolve(null));
        Assert.Equal(PetState.Talk, EmotionMap.Resolve(""));
    }
}
