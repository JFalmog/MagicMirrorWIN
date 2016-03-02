﻿using MagicMirror.Factory;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Media.SpeechRecognition;
using Windows.Storage;
using Windows.UI.Xaml;

namespace MagicMirror.ViewModel
{
    public class MainViewModel : BaseViewModel
    {
        // compliments
        public Compliments Compliments { get; set; } = new Compliments();

        // calendar / time
        public Calendar Calendar { get; set; } = new Calendar();

        // weather
        public Weather Weather { get; set; } = new Weather();

        // news
        public News News { get; set; } = new News();
        
        // splash
        private bool _ShowSplashScreen = true;
        public bool ShowSplashScreen
        {
            get { return _ShowSplashScreen; }
            set
            {
                if (value != _ShowSplashScreen)
                {
                    _ShowSplashScreen = value;
                    RaisePropertyChanged("ShowSplashScreen");
                }
            }
        }


        // this commadn is for testing purpose if no microphone or whatsoever
        public RelayCommand<object> Clicked { get; set; }
        // this command will start our update mechanism and init some basics e.g. speech recognition
        public RelayCommand<object> Initzialize { get; set; }

        private IUpdateViewModel[] _updateViewModels;

        public MainViewModel()
        {
            _updateViewModels = new IUpdateViewModel[] {
                Compliments, Calendar, Weather, News
            };
            // this commadn is for testing purpose if no microphone or whatsoever
            Clicked = new RelayCommand<object>(() => {
                if (this.News.ShowDetail)
                    this.News.HideDetail();
                else
                    this.News.ViewDetail();
            });

            Initzialize = new RelayCommand<object>(() =>
            {
                InitializeSpeechRecognizer();

                StartUpdateTask();
            });
        }
        

        async void StartUpdateTask()
        {
            await Task.Factory.StartNew(async() => {
                while (true)
                {
                    try
                    {
                        await Process();
                        await Task.Delay(60000);
                    }
                    catch (Exception ex)
                    {
                        Log.e(ex);
                    }
                }
            });
        }

        #region DATA Processing
        //              TIMEOUTS
        DateTime _dtUpdateCalendar = DateTime.MinValue;
        DateTime _dtUpdateWeather = DateTime.MinValue;
        DateTime _dtUpdateNews = DateTime.MinValue;
        private async Task Process()
        {
            var config = new Configuration.Configuration();

            foreach (var updateViewModel in _updateViewModels)
            {
                var now = DateTime.Now;
                if ((now - updateViewModel.LastUpdate) > updateViewModel.UpdateTimeout)
                {
                    updateViewModel.LastUpdate = now;

                    object dat = null;

                    try
                    {
                        dat = await updateViewModel.ProcessData(config);
                    }
                    catch (Exception ex)
                    {
                        Log.e(ex);
                    }                    

                    if (dat != null)
                    {
                        await updateViewModel.UILock.WaitAsync();
                        try
                        {
                            await EnsureOnUI(() => updateViewModel.UpdateUI(config, dat));
                        }
                        catch (Exception ex)
                        {
                            Log.e(ex);
                        }
                        updateViewModel.UILock.Release();
                    }
                }
            }
            

            if (ShowSplashScreen)
                await EnsureOnUI(() => ShowSplashScreen = false);
        }
        #endregion
        
        private static async Task EnsureOnUI(Windows.UI.Core.DispatchedHandler callback)
        {
            if (App.Dispatcher.HasThreadAccess)
            {
                callback.Invoke();
            }
            else
            {
                await App.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, callback);
            }
        }

        #region SpeechRecognizer
        private SpeechRecognizer recognizer;
        bool isSpeechInizialized = false;

        private bool _ShowListeningInfo;
        public bool ShowListeningInfo
        {
            get { return _ShowListeningInfo; }
            set
            {
                if (value != _ShowListeningInfo)
                {
                    _ShowListeningInfo = value;
                    RaisePropertyChanged("ShowListeningInfo");
                }
            }
        }

        private async void InitializeSpeechRecognizer()
        {
            if (isSpeechInizialized) return;

            // init recognizer
            recognizer = new SpeechRecognizer();
            recognizer.Constraints.Add(new SpeechRecognitionListConstraint(new string[] 
            {
                "Show", "News", "Detail",
                "Hide", "Close", "Weather", "Time", "Back", "Escape"
            }));

            recognizer.StateChanged += RecognizerStateChanged;
            recognizer.ContinuousRecognitionSession.ResultGenerated += RecognizerResultGenerated;
            
            // compile constraints
            SpeechRecognitionCompilationResult compilationResult = await recognizer.CompileConstraintsAsync();
            
            // start recogition session if successful
            if (compilationResult.Status == SpeechRecognitionResultStatus.Success)
            {
                Log.i("SR Success");

                await recognizer.ContinuousRecognitionSession.StartAsync();
            }
            else
            {
                Log.w("SR Failed {0}", compilationResult.Status);
            }

            isSpeechInizialized = true;
        }
        private async void RecognizerResultGenerated(SpeechContinuousRecognitionSession session, SpeechContinuousRecognitionResultGeneratedEventArgs args)
        {
            Log.i(args.Result.Status.ToString());
            Log.i(args.Result.Text);

            if (args.Result.Status == SpeechRecognitionResultStatus.Success)
            {
                string text = args.Result.Text;

                if (!string.IsNullOrEmpty(text))
                {
                    text = text.ToUpper();

                    if (text == "SHOW" || text == "NEWS" || text == "DETAIL")
                    {
                        await EnsureOnUI(() => this.News.ViewDetail());
                    }
                    else if (text == "HIDE" || text == "CLOSE" || text == "WEATHER" || text == "TIME" || text == "BACK" || text == "ESCAPE")
                    {
                        await EnsureOnUI(() => this.News.HideDetail());
                    }
                }
            }

        }
        private async void RecognizerStateChanged(SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
        {
            Log.i("SR State: " + args.State.ToString());

            await EnsureOnUI(() => {
                ShowListeningInfo = args.State == SpeechRecognizerState.SpeechDetected;
            });
        }
        #endregion
    }
}