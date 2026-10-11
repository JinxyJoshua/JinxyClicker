using Xunit;

namespace JinxyClicker;

public class RobloxSensTests
{
    private const string Sample =
        "<Item class=\"UserGameSettings\"><Properties>\n" +
        "  <float name=\"GamepadCameraSensitivity\">0.2001</float>\n" +
        "  <float name=\"MouseSensitivity\">0.200159997</float>\n" +
        "  <Vector2 name=\"MouseSensitivityFirstPerson\">\n" +
        "    <X>0.200159997</X>\n" +
        "    <Y>0.200159997</Y>\n" +
        "  </Vector2>\n" +
        "  <bool name=\"Fullscreen\">false</bool>\n" +
        "</Properties></Item>";

    [Fact]
    public void SetSensitivity_updates_general_and_first_person_values()
    {
        string result = RobloxSens.SetSensitivityInXml(Sample, 0.5f);

        Assert.Contains("<float name=\"MouseSensitivity\">0.5</float>", result);
        Assert.Contains("<X>0.5</X>", result);
        Assert.Contains("<Y>0.5</Y>", result);
    }

    [Fact]
    public void SetSensitivity_leaves_other_settings_untouched()
    {
        string result = RobloxSens.SetSensitivityInXml(Sample, 1.25f);

        // The gamepad sensitivity and unrelated settings must not change.
        Assert.Contains("<float name=\"GamepadCameraSensitivity\">0.2001</float>", result);
        Assert.Contains("<bool name=\"Fullscreen\">false</bool>", result);
    }
}
