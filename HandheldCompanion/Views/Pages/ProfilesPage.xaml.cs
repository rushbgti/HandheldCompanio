using ControllerCommon;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ModernWpf.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using Page = System.Windows.Controls.Page;

namespace HandheldCompanion.Views.Pages
{
    /// <summary>
    /// Interaction logic for Profiles.xaml
    /// </summary>
    public partial class ProfilesPage : Page
    {
        private MainWindow mainWindow;
        private ILogger microsoftLogger;

        private ProfileManager profileManager;
        private Profile profileCurrent;

        private Dictionary<GamepadButtonFlags, CheckBox> activators = new();

        // pipe vars
        PipeClient pipeClient;

        public ProfilesPage()
        {
            InitializeComponent();
        }

        public ProfilesPage(string Tag, MainWindow mainWindow, ILogger microsoftLogger) : this()
        {
            this.Tag = Tag;

            this.mainWindow = mainWindow;
            this.microsoftLogger = microsoftLogger;

            this.pipeClient = mainWindow.pipeClient;
            this.pipeClient.ServerMessage += PipeClient_ServerMessage;

            this.profileManager = mainWindow.profileManager;

            // initialize Profile Manager
            profileManager.Deleted += ProfileDeleted;
            profileManager.Updated += ProfileUpdated;
            profileManager.Start();

            foreach (Input mode in (Input[])Enum.GetValues(typeof(Input)))
                cB_Input.Items.Add(Utils.GetDescriptionFromEnumValue(mode));

            foreach (Output mode in (Output[])Enum.GetValues(typeof(Output)))
                cB_Output.Items.Add(Utils.GetDescriptionFromEnumValue(mode));

            int idx = 0;
            foreach (GamepadButtonFlags button in (GamepadButtonFlags[])Enum.GetValues(typeof(GamepadButtonFlags)))
            {
                CheckBox checkbox = new CheckBox()
                {
                    Tag = button,
                    Content = Utils.GetDescriptionFromEnumValue(button)
                };

                ((SimpleStackPanel)cB_Buttons.Children[idx]).Children.Add(checkbox);

                activators.Add(button, checkbox);

                idx = idx < 3 ? idx += 1 : 0;
            }

            // select default profile
            cB_Profiles.SelectedItem = profileCurrent = profileManager.GetDefault();
        }

        private void PipeClient_ServerMessage(object sender, PipeMessage e)
        {
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
        }

        #region UI
        public void ProfileUpdated(Profile profile)
        {
            // inform Service we have a new default profile
            if (profile.IsDefault)
                pipeClient.SendMessage(new PipeClientProfile() { profile = profile });

            this.Dispatcher.Invoke(async () =>
            {
                int idx = -1;
                foreach (Profile pr in cB_Profiles.Items)
                    if (pr.executable == profile.executable)
                    {
                        idx = cB_Profiles.Items.IndexOf(pr);
                        break;
                    }

                if (idx != -1)
                    cB_Profiles.Items[idx] = profile;
                else
                    cB_Profiles.Items.Add(profile);
                cB_Profiles.SelectedItem = profile;
            });
        }

        public void ProfileDeleted(Profile profile)
        {
            this.Dispatcher.Invoke(() =>
            {
                int idx = cB_Profiles.Items.IndexOf(profile);
                if (idx != -1)
                    cB_Profiles.Items.RemoveAt(idx);
            });
        }
        #endregion

        private async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var path = openFileDialog.FileName;
                    var folder = Path.GetDirectoryName(path);

                    var file = openFileDialog.SafeFileName;
                    var ext = Path.GetExtension(file);

                    switch (ext)
                    {
                        default:
                        case ".exe":
                            break;
                        case ".xml":
                            try
                            {
                                XmlDocument doc = new XmlDocument();
                                doc.Load(path);

                                XmlNodeList Applications = doc.GetElementsByTagName("Applications");
                                foreach (XmlNode node in Applications)
                                {
                                    foreach (XmlNode child in node.ChildNodes)
                                    {
                                        if (child.Name.Equals("Application"))
                                        {
                                            if (child.Attributes != null)
                                            {
                                                foreach (XmlAttribute attribute in child.Attributes)
                                                {
                                                    switch (attribute.Name)
                                                    {
                                                        case "Executable":
                                                            path = Path.Combine(folder, attribute.InnerText);
                                                            file = Path.GetFileName(path);
                                                            break;
                                                    }
                                                    continue;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                microsoftLogger.LogError(ex.Message, true);
                            }
                            break;
                    }

                    Profile profile = new Profile(path);

                    bool exists = false;

                    if (profileManager.Contains(profile))
                    {
                        // todo: implement localized strings
                        Task<ContentDialogResult> result = Dialog.ShowAsync("Overwrite profile permanently?", "A profile with the same executable has been fund. Do you want to overwrite it?", ContentDialogButton.Primary, "Close", "Yes");
                        await result; // sync call

                        switch (result.Result)
                        {
                            case ContentDialogResult.Primary:
                                exists = false;
                                break;
                            default:
                                exists = true;
                                break;
                        }
                    }

                    if (!exists)
                    {
                        profileManager.UpdateProfile(profile);
                        profileManager.SerializeProfile(profile);
                    }
                }
                catch (Exception ex)
                {
                    microsoftLogger.LogError(ex.Message);
                }
            }
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            Page page;
            switch ((Input)cB_Input.SelectedIndex)
            {
                default:
                case Input.JoystickCamera:
                    page = new ProfileSettingsMode0("ProfileSettingsMode0", profileCurrent, pipeClient);
                    break;
                case Input.JoystickSteering:
                    page = new ProfileSettingsMode1("ProfileSettingsMode1", profileCurrent, pipeClient);
                    break;
            }
            mainWindow.NavView_Navigate(page);
        }

        private void cB_Profiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            profileCurrent = (Profile)cB_Profiles.SelectedItem;

            if (profileCurrent == null)
                return;

            Dispatcher.BeginInvoke(() =>
            {
                // disable button if is default profile
                b_DeleteProfile.IsEnabled = !profileCurrent.IsDefault;
                tB_ProfileName.IsEnabled = !profileCurrent.IsDefault;
                cB_ExclusiveHook.IsEnabled = !profileCurrent.IsDefault;

                // populate controls
                tB_ProfileName.Text = profileCurrent.name;
                tB_ProfilePath.Text = profileCurrent.fullpath;
                Toggle_UniversalMotion.IsOn = profileCurrent.umc_enabled;
                tb_ProfileGyroValue.Value = profileCurrent.gyrometer;
                tb_ProfileAcceleroValue.Value = profileCurrent.accelerometer;
                cB_GyroSteering.SelectedIndex = profileCurrent.steering;
                cB_InvertVertical.IsChecked = profileCurrent.invertvertical;
                cB_InvertHorizontal.IsChecked = profileCurrent.inverthorizontal;
                cB_Input.SelectedIndex = (int)profileCurrent.umc_input;
                cB_Output.SelectedIndex = (int)profileCurrent.umc_output;
                cB_EnableHook.IsChecked = profileCurrent.mousehook_enabled;
                cB_ExclusiveHook.IsChecked = profileCurrent.mousehook_exclusive;
                cB_Whitelist.IsChecked = profileCurrent.whitelisted;
                cB_Wrapper.IsChecked = profileCurrent.use_wrapper;

                foreach (GamepadButtonFlags button in (GamepadButtonFlags[])Enum.GetValues(typeof(GamepadButtonFlags)))
                    if (profileCurrent.umc_trigger.HasFlag(button))
                        activators[button].IsChecked = true;
                    else
                        activators[button].IsChecked = false;

                // display warnings
                ProfileErrorCode currentError = profileCurrent.error;
                if (profileCurrent.IsRunning)
                    currentError = ProfileErrorCode.IsRunning;

                switch (currentError)
                {
                    default:
                    case ProfileErrorCode.None:
                        WarningBorder.Visibility = Visibility.Collapsed;
                        cB_Whitelist.IsEnabled = true;
                        cB_Wrapper.IsEnabled = true;
                        break;

                    case ProfileErrorCode.MissingExecutable:
                    case ProfileErrorCode.MissingPath:
                    case ProfileErrorCode.MissingPermission:
                    case ProfileErrorCode.IsDefault:
                        WarningBorder.Visibility = Visibility.Visible;
                        WarningContent.Text = Utils.GetDescriptionFromEnumValue(currentError);
                        cB_Whitelist.IsEnabled = false; // you can't whitelist an application without path
                        cB_Wrapper.IsEnabled = false;   // you can't deploy wrapper on an application without path
                        break;

                    case ProfileErrorCode.IsRunning:
                        WarningBorder.Visibility = Visibility.Visible;
                        WarningContent.Text = Utils.GetDescriptionFromEnumValue(currentError);
                        cB_Whitelist.IsEnabled = true; // you can't whitelist an application without path
                        cB_Wrapper.IsEnabled = false;   // you can't deploy wrapper on a running application
                        break;
                }
            });
        }

        private void b_DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            Profile profile = (Profile)cB_Profiles.SelectedItem;
            profileManager.DeleteProfile(profile);

            cB_Profiles.SelectedIndex = 0;
        }

        private void b_ApplyProfile_Click(object sender, RoutedEventArgs e)
        {
            if (profileCurrent == null)
                return;

            // todo: implement localized strings
            Dialog.ShowAsync("Profile updated", $"{profileCurrent.name} was updated.", ContentDialogButton.Primary, null, "OK");

            profileCurrent.name = tB_ProfileName.Text;
            profileCurrent.fullpath = tB_ProfilePath.Text;

            profileCurrent.gyrometer = (float)tb_ProfileGyroValue.Value;
            profileCurrent.accelerometer = (float)tb_ProfileAcceleroValue.Value;
            profileCurrent.whitelisted = (bool)cB_Whitelist.IsChecked;
            profileCurrent.use_wrapper = (bool)cB_Wrapper.IsChecked;

            profileCurrent.steering = cB_GyroSteering.SelectedIndex;

            profileCurrent.invertvertical = (bool)cB_InvertVertical.IsChecked;
            profileCurrent.inverthorizontal = (bool)cB_InvertHorizontal.IsChecked;

            profileCurrent.umc_enabled = (bool)Toggle_UniversalMotion.IsOn;

            profileCurrent.umc_input = (Input)cB_Input.SelectedIndex;
            profileCurrent.umc_output = (Output)cB_Output.SelectedIndex;

            // Touch settings
            profileCurrent.mousehook_enabled = (bool)cB_EnableHook.IsChecked;
            profileCurrent.mousehook_exclusive = (bool)cB_ExclusiveHook.IsChecked;

            profileCurrent.umc_trigger = 0;

            foreach (GamepadButtonFlags button in (GamepadButtonFlags[])Enum.GetValues(typeof(GamepadButtonFlags)))
                if ((bool)activators[button].IsChecked)
                    profileCurrent.umc_trigger |= button;

            profileManager.profiles[profileCurrent.name] = profileCurrent;
            profileManager.UpdateProfile(profileCurrent);
            profileManager.SerializeProfile(profileCurrent);
        }

        private void cB_Whitelist_Checked(object sender, RoutedEventArgs e)
        {
            Expander_UMC.IsEnabled = (bool)!cB_Whitelist.IsChecked;
        }

        private void cB_Wrapper_Checked(object sender, RoutedEventArgs e)
        {
            // do something
        }

        private void cB_EnableHook_Checked(object sender, RoutedEventArgs e)
        {
            // do something
        }

        private void cB_ExclusiveHook_Checked(object sender, RoutedEventArgs e)
        {
            // do something
        }

        private void Toggle_UniversalMotion_Toggled(object sender, RoutedEventArgs e)
        {
            cB_Whitelist.IsEnabled = !(bool)Toggle_UniversalMotion.IsOn && !profileCurrent.IsDefault;
            Expander_UMC.IsExpanded = Toggle_UniversalMotion.IsOn;
        }

        private void cB_Buttons_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            MainWindow.scrollLock = true;
        }

        private void cB_Buttons_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            MainWindow.scrollLock = false;
        }

        private void Expander_Expanded(object sender, RoutedEventArgs e)
        {
            ((Expander)sender).BringIntoView();
        }
    }
}