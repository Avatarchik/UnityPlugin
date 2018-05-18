#if UNITY_EDITOR

using System;

using UnityEditor;
using UnityEngine;

namespace ModIO
{
    public class LoginWindow : EditorWindow
    {
        [MenuItem("mod.io/Login")]
        public static void ShowWindow()
        {
            GetWindow<LoginWindow>("Login to mod.io");
        }

        // ---------[ MEMBERS ]---------
        public static event Action<UserProfile> userLoggedIn;
        private static bool isAwaitingServerResponse = false;

        private bool isInputtingEmail;
        private string emailAddressInput;
        private string securityCodeInput;
        private bool isLoggedIn;

        private string helpMessage = string.Empty;
        private MessageType helpType = MessageType.Info;

        // ---------[ INITIALIZATION ]---------
        protected virtual void OnEnable()
        {
            isInputtingEmail = true;
            emailAddressInput = "";
            securityCodeInput = "";
            isLoggedIn = false;
        }

        protected virtual void OnGUI()
        {
            // TODO(@jackson): Find a way to reselect the inputfield
            EditorGUILayout.LabelField("LOG IN TO/REGISTER YOUR MOD.IO ACCOUNT");

            using (new EditorGUI.DisabledScope(isAwaitingServerResponse || isLoggedIn))
            {
                EditorGUILayout.BeginHorizontal();
                {
                    using (new EditorGUI.DisabledScope(isInputtingEmail))
                    {
                        if(GUILayout.Button("Email"))
                        {
                            isInputtingEmail = true;
                        }
                    }
                    using (new EditorGUI.DisabledScope(!isInputtingEmail))
                    {
                        if(GUILayout.Button("Security Code"))
                        {
                            isInputtingEmail = false;
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                if(isInputtingEmail)
                {
                    emailAddressInput = EditorGUILayout.TextField("Email Address", emailAddressInput);
                }
                else
                {
                    securityCodeInput = EditorGUILayout.TextField("Security Code", securityCodeInput);
                }

                EditorGUILayout.BeginHorizontal();
                {
                    GUI.SetNextControlName("SubmitButton");
                    if(GUILayout.Button("Submit"))
                    {
                        isAwaitingServerResponse = true;
                        GUI.FocusControl("SubmitButton");

                        Action<string, MessageType> endRequestSendingAndInputEmail = (m, t) =>
                        {
                            isAwaitingServerResponse = false;
                            isInputtingEmail = true;
                            helpMessage = m;
                            helpType = t;
                            Repaint();
                        };

                        Action<string, MessageType> endRequestSendingAndInputCode = (m, t) =>
                        {
                            isAwaitingServerResponse = false;
                            isInputtingEmail = false;
                            helpMessage = m;
                            helpType = t;
                            Repaint();
                        };

                        if(isInputtingEmail)
                        {
                            securityCodeInput = "";

                            ModManager.RequestSecurityCode(emailAddressInput,
                                                           m => endRequestSendingAndInputCode(m.message, MessageType.Info),
                                                           e => endRequestSendingAndInputEmail(ConvertErrorToHelpString(e), MessageType.Error));
                        }
                        else
                        {
                            Action<UserProfile> onGetUserProfile = (u) =>
                            {
                                CacheClient.SaveAuthenticatedUserProfile(u);
                                helpMessage = ("Welcome " + u.username
                                               + "! You have successfully logged in."
                                               + " Feel free to close this window.");
                                isLoggedIn = true;

                                LoginWindow.isAwaitingServerResponse = false;
                                Repaint();

                                if(userLoggedIn != null)
                                {
                                    userLoggedIn(u);
                                }
                            };

                            Action<string> onTokenReceived = (token) =>
                            {
                                CacheClient.SaveAuthenticatedUserToken(token);
                                APIClient.userAuthorizationToken = token;

                                APIClient.GetAuthenticatedUser(onGetUserProfile,
                                                               e => endRequestSendingAndInputCode(ConvertErrorToHelpString(e), MessageType.Error));
                            };

                            APIClient.RequestOAuthToken(securityCodeInput,
                                                        onTokenReceived,
                                                        e => endRequestSendingAndInputCode(ConvertErrorToHelpString(e), MessageType.Error));
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            if(!String.IsNullOrEmpty(helpMessage))
            {
                EditorGUILayout.HelpBox(helpMessage,
                                        helpType);
            }
        }

        private string ConvertErrorToHelpString(WebRequestError error)
        {
            if(error.fieldValidationMessages != null
               && error.fieldValidationMessages.Count > 0)
            {
                var helpString = new System.Text.StringBuilder();

                foreach(string message in error.fieldValidationMessages.Values)
                {
                    helpString.Append(message + "\n");
                }

                helpString.Length -= 1;

                return helpString.ToString();
            }
            else
            {
                return error.message;
            }
        }
    }
}

#endif
