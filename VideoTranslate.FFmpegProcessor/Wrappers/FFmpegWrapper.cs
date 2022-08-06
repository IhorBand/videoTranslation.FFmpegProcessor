﻿using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using VideoTranslate.FFmpegProcessor.Models.Configurations;

namespace VideoTranslate.FFmpegProcessor.Wrappers
{
    public class FFmpegWrapper
    {
        private readonly FFmpegConfiguration ffmpegConfiguration;
        #region Properties

        #endregion

        #region Constructors
        public FFmpegWrapper(FFmpegConfiguration fFmpegConfiguration)
        {
            this.ffmpegConfiguration = ffmpegConfiguration ?? throw new ArgumentNullException("ffmpegConfiguration", "No value was found in the app setting for ffmpegConfiguration");    
            //first make sure we have a value for the ffexe file setting
            if (string.IsNullOrEmpty(ffmpegConfiguration.FFmpegLocation))
            {
                throw new Exception("No value was found in the app setting for FFmpeg:FFmpegLocation");
            }

            //Now see if ffmpeg.exe exists
            string workingpath = GetWorkingFile();
            if (string.IsNullOrEmpty(workingpath))
            {
                //ffmpeg doesn't exist at the location stated.
                throw new Exception("Could not find a copy of ffmpeg.exe");
            }
            this.ffmpegConfiguration.FFmpegLocation = workingpath;

            //now see if we have a temporary place to work
            if (string.IsNullOrEmpty(ffmpegConfiguration.WorkingPath))
            {
                throw new Exception("No value was found in the app setting for FFmpeg:WorkingPath");
            }
        }
        #endregion

        #region Initialization

        private string GetWorkingFile()
        {
            //try the stated directory
            if (File.Exists(this.ffmpegConfiguration.FFmpegLocation))
            {
                return this.ffmpegConfiguration.FFmpegLocation;
            }

            //oops, that didn't work, try the base directory
            if (File.Exists(Path.GetFileName(this.ffmpegConfiguration.FFmpegLocation)))
            {
                return Path.GetFileName(this.ffmpegConfiguration.FFmpegLocation);
            }

            //well, now we are really unlucky, let's just return null
            return null;
        }
        #endregion

        #region Get the File without creating a file lock
        public static System.Drawing.Image LoadImageFromFile(string fileName)
        {
            System.Drawing.Image theImage = null;
            using (FileStream fileStream = new FileStream(fileName, FileMode.Open,
            FileAccess.Read))
            {
                byte[] img;
                img = new byte[fileStream.Length];
                fileStream.Read(img, 0, img.Length);
                fileStream.Close();
                theImage = System.Drawing.Image.FromStream(new MemoryStream(img));
                img = null;
            }
            GC.Collect();
            return theImage;
        }

        public static MemoryStream LoadMemoryStreamFromFile(string fileName)
        {
            MemoryStream ms = null;
            using (FileStream fileStream = new FileStream(fileName, FileMode.Open,
            FileAccess.Read))
            {
                byte[] fil;
                fil = new byte[fileStream.Length];
                fileStream.Read(fil, 0, fil.Length);
                fileStream.Close();
                ms = new MemoryStream(fil);
            }
            GC.Collect();
            return ms;
        }
        #endregion

        #region Run the process
        private string RunProcess(string Parameters)
        {
            //create a process info
            ProcessStartInfo oInfo = new ProcessStartInfo(this.ffmpegConfiguration.FFmpegLocation, Parameters);
            oInfo.UseShellExecute = false;
            oInfo.CreateNoWindow = true;
            oInfo.RedirectStandardOutput = true;
            oInfo.RedirectStandardError = true;

            //Create the output and streamreader to get the output
            string output = null; StreamReader srOutput = null;

            //try the process
            try
            {
                //run the process
                Process proc = System.Diagnostics.Process.Start(oInfo);

                proc.WaitForExit();

                //get the output
                srOutput = proc.StandardError;

                //now put it in a string
                output = srOutput.ReadToEnd();

                proc.Close();
            }
            catch (Exception)
            {
                output = string.Empty;
            }
            finally
            {
                //now, if we succeded, close out the streamreader
                if (srOutput != null)
                {
                    srOutput.Close();
                    srOutput.Dispose();
                }
            }
            return output;
        }
        #endregion

        #region GetVideoInfo
        public VideoFile GetVideoInfo(MemoryStream inputFile, string Filename)
        {
            string tempfile = Path.Combine(this.ffmpegConfiguration.WorkingPath, System.Guid.NewGuid().ToString() + Path.GetExtension(Filename));
            FileStream fs = File.Create(tempfile);
            inputFile.WriteTo(fs);
            fs.Flush();
            fs.Close();
            GC.Collect();

            VideoFile vf = null;
            try
            {
                vf = new VideoFile(tempfile);
            }
            catch (Exception ex)
            {
                throw ex;
            }

            GetVideoInfo(vf);

            try
            {
                File.Delete(tempfile);
            }
            catch (Exception)
            {

            }

            return vf;
        }
        public VideoFile GetVideoInfo(string inputPath)
        {
            VideoFile vf = null;
            try
            {
                vf = new VideoFile(inputPath);
            }
            catch (Exception ex)
            {
                throw ex;
            }
            GetVideoInfo(vf);
            return vf;
        }
        public void GetVideoInfo(VideoFile input)
        {
            //set up the parameters for video info
            string Params = string.Format("-i {0}", input.Path);
            string output = RunProcess(Params);
            input.RawInfo = output;

            //get duration
            Regex re = new Regex("[D|d]uration:.((\\d|:|\\.)*)");
            Match m = re.Match(input.RawInfo);

            if (m.Success)
            {
                string duration = m.Groups[1].Value;
                string[] timepieces = duration.Split(new char[] { ':', '.' });
                if (timepieces.Length == 4)
                {
                    input.Duration = new TimeSpan(0, Convert.ToInt16(timepieces[0]), Convert.ToInt16(timepieces[1]), Convert.ToInt16(timepieces[2]), Convert.ToInt16(timepieces[3]));
                }
            }

            //get audio bit rate
            re = new Regex("[B|b]itrate:.((\\d|:)*)");
            m = re.Match(input.RawInfo);
            double kb = 0.0;
            if (m.Success)
            {
                Double.TryParse(m.Groups[1].Value, out kb);
            }
            input.BitRate = kb;

            //get the audio format
            re = new Regex("[A|a]udio:.*");
            m = re.Match(input.RawInfo);
            if (m.Success)
            {
                input.AudioFormat = m.Value;
            }

            //get the video format
            re = new Regex("[V|v]ideo:.*");
            m = re.Match(input.RawInfo);
            if (m.Success)
            {
                input.VideoFormat = m.Value;
            }

            //get the video format
            re = new Regex("(\\d{2,3})x(\\d{2,3})");
            m = re.Match(input.RawInfo);
            if (m.Success)
            {
                int width = 0; int height = 0;
                int.TryParse(m.Groups[1].Value, out width);
                int.TryParse(m.Groups[2].Value, out height);
                input.Width = width;
                input.Height = height;
            }
            input.infoGathered = true;
        }
        #endregion

        #region Convert to FLV
        public OutputPackage ConvertToFLV(MemoryStream inputFile, string Filename)
        {
            string tempfile = Path.Combine(this.ffmpegConfiguration.WorkingPath, System.Guid.NewGuid().ToString() + Path.GetExtension(Filename));
            FileStream fs = File.Create(tempfile);
            inputFile.WriteTo(fs);
            fs.Flush();
            fs.Close();
            GC.Collect();

            VideoFile vf = null;
            try
            {
                vf = new VideoFile(tempfile);
            }
            catch (Exception ex)
            {
                throw ex;
            }

            OutputPackage oo = ConvertToFLV(vf);

            try
            {
                File.Delete(tempfile);
            }
            catch (Exception)
            {

            }

            return oo;
        }
        public OutputPackage ConvertToFLV(string inputPath)
        {
            VideoFile vf = null;
            try
            {
                vf = new VideoFile(inputPath);
            }
            catch (Exception ex)
            {
                throw ex;
            }

            OutputPackage oo = ConvertToFLV(vf);
            return oo;
        }
        public OutputPackage ConvertToFLV(VideoFile input)
        {
            if (!input.infoGathered)
            {
                GetVideoInfo(input);
            }
            OutputPackage ou = new OutputPackage();

            //set up the parameters for getting a previewimage
            string filename = System.Guid.NewGuid().ToString() + ".jpg";
            int secs;

            //divide the duration in 3 to get a preview image in the middle of the clip
            //instead of a black image from the beginning.
            secs = (int)Math.Round(TimeSpan.FromTicks(input.Duration.Ticks / 3).TotalSeconds, 0);

            string finalpath = Path.Combine(this.ffmpegConfiguration.WorkingPath, filename);
            string Params = string.Format("-i {0} {1} -vcodec mjpeg -ss {2} -vframes 1 -an -f rawvideo", input.Path, finalpath, secs);
            string output = RunProcess(Params);

            ou.RawOutput = output;

            if (File.Exists(finalpath))
            {
                ou.PreviewImage = LoadImageFromFile(finalpath);
                try
                {
                    File.Delete(finalpath);
                }
                catch (Exception) { }
            }
            else
            { //try running again at frame 1 to get something
                Params = string.Format("-i {0} {1} -vcodec mjpeg -ss {2} -vframes 1 -an -f rawvideo", input.Path, finalpath, 1);
                output = RunProcess(Params);

                ou.RawOutput = output;

                if (File.Exists(finalpath))
                {
                    ou.PreviewImage = LoadImageFromFile(finalpath);
                    try
                    {
                        File.Delete(finalpath);
                    }
                    catch (Exception) { }
                }
            }

            finalpath = Path.Combine(this.ffmpegConfiguration.WorkingPath, filename);
            filename = System.Guid.NewGuid().ToString() + ".flv";
            Params = string.Format("-i {0} -y -ar 22050 -ab 64 -f flv {1}", input.Path, finalpath);
            output = RunProcess(Params);

            if (File.Exists(finalpath))
            {
                ou.VideoStream = LoadMemoryStreamFromFile(finalpath);
                try
                {
                    File.Delete(finalpath);
                }
                catch (Exception) { }
            }
            return ou;
        }

        public OutputPackage GetWavFromVideo(string inputPath, string outputPath)
        {

        }
        #endregion
    }

    public class VideoFile
    {
        #region Properties
        private string _Path;
        public string Path
        {
            get
            {
                return _Path;
            }
            set
            {
                _Path = value;
            }
        }

        public TimeSpan Duration { get; set; }
        public double BitRate { get; set; }
        public string AudioFormat { get; set; }
        public string VideoFormat { get; set; }
        public int Height { get; set; }
        public int Width { get; set; }
        public string RawInfo { get; set; }
        public bool infoGathered { get; set; }
        #endregion

        #region Constructors
        public VideoFile(string path)
        {
            _Path = path;
            Initialize();
        }
        #endregion

        #region Initialization
        private void Initialize()
        {
            this.infoGathered = false;
            //first make sure we have a value for the video file setting
            if (string.IsNullOrEmpty(_Path))
            {
                throw new Exception("Could not find the location of the video file");
            }

            //Now see if the video file exists
            if (!File.Exists(_Path))
            {
                throw new Exception("The video file " + _Path + " does not exist.");
            }
        }
        #endregion
    }

    public class OutputPackage
    {
        public MemoryStream VideoStream { get; set; }
        public System.Drawing.Image PreviewImage { get; set; }
        public string RawOutput { get; set; }
        public bool Success { get; set; }
    }
}