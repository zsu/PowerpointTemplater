﻿namespace PowerpointTemplater
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;

    using DocumentFormat.OpenXml;
    using DocumentFormat.OpenXml.Packaging;
    using DocumentFormat.OpenXml.Presentation;

    /// <summary>
    /// Represents a PowerPoint file.
    /// </summary>
    /// <returns>Follows the facade pattern.</returns>
    public sealed class Powerpoint : IDisposable
    {
        private readonly PresentationDocument presentationDocument;

        /// <summary>
        /// Regex pattern to extract tags from templates.
        /// </summary>
        public static readonly Regex TagPattern = new Regex(@"{{[A-Za-z0-9_+\-\.]*}}");

        /// <summary>
        /// MIME type for PowerPoint Powerpoint files.
        /// </summary>
        public const string MimeType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

        #region ctor

        /// <summary>
        /// Initializes a new instance of the <see cref="Powerpoint"/> class.
        /// </summary>
        /// <param name="file">The PowerPoint file.</param>
        /// <param name="access">Access mode to use to open the PowerPoint file.</param>
        /// <remarks>Opens a PowerPoint file in read-write (default) or read only mode.</remarks>
        public Powerpoint(string file, FileAccess access)
        {
            bool isEditable = false;
            switch (access)
            {
                case FileAccess.Read:
                    isEditable = false;
                    break;
                case FileAccess.Write:
                case FileAccess.ReadWrite:
                    isEditable = true;
                    break;
            }

            this.presentationDocument = PresentationDocument.Open(file, isEditable);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Powerpoint"/> class.
        /// </summary>
        /// <param name="stream">The PowerPoint stream.</param>
        /// <param name="access">Access mode to use to open the PowerPoint file.</param>
        /// <remarks>Opens a PowerPoint stream in read-write (default) or read only mode.</remarks>
        public Powerpoint(Stream stream, FileAccess access)
        {
            bool isEditable = false;
            switch (access)
            {
                case FileAccess.Read:
                    isEditable = false;
                    break;
                case FileAccess.Write:
                case FileAccess.ReadWrite:
                    isEditable = true;
                    break;
            }

            this.presentationDocument = PresentationDocument.Open(stream, isEditable);
        }

        #endregion ctor

        public void Dispose()
        {
            this.Close();
        }

        /// <summary>
        /// Closes the PowerPoint file.
        /// </summary>
        /// <remarks>
        /// 99% of the time this is not needed, the PowerPoint file will get closed when the destructor is being called.
        /// </remarks>
        public void Close()
        {
            this.presentationDocument.Dispose();
        }

        /// <summary>
        /// Counts the number of slides.
        /// </summary>
        /// <returns>The number of slides.</returns>
        /// <remarks>
        /// <see href="http://msdn.microsoft.com/en-us/library/office/gg278331">How to: Get All the Text in All Slides in a Presentation</see>
        /// </remarks>
        public int SlidesCount()
        {
            PresentationPart presentationPart = this.presentationDocument.PresentationPart;
            return presentationPart.SlideParts.Count();
        }

        /// <summary>
        /// Finds the slides matching a given note.
        /// </summary>
        /// <param name="note">Note to match the slide with.</param>
        /// <returns>The matching slides.</returns>
        public IEnumerable<PowerpointSlide> FindSlides(string note)
        {
            List<PowerpointSlide> slides = new List<PowerpointSlide>();

            for (int i = 0; i < this.SlidesCount(); i++)
            {
                PowerpointSlide slide = this.GetSlide(i);
                IEnumerable<string> notes = slide.GetNotes();
                foreach (string tmp in notes)
                {
                    if (tmp.Contains(note))
                    {
                        slides.Add(slide);
                        break;
                    }
                }
            }

            return slides;
        }

        /// <summary>
        /// Gets the thumbnail (PNG format) associated with the PowerPoint file.
        /// </summary>
        /// <param name="size">The size of the thumbnail to generate, default is 256x192 pixels in 4:3 (160x256 in 16:10 portrait).</param>
        /// <returns>The thumbnail as a byte array (PNG format).</returns>
        /// <remarks>
        /// Even if the PowerPoint file does not contain any slide, still a thumbnail is generated.
        /// If the given size is superior to the default size then the thumbnail is upscaled and looks blurry so don't do it.
        /// </remarks>
        public byte[] GetThumbnail(Size size = default(Size))
        {
            byte[] thumbnail;

            var thumbnailPart = this.presentationDocument.ThumbnailPart;
            using (var stream = thumbnailPart.GetStream(FileMode.Open, FileAccess.Read))
            {
                var image = Image.FromStream(stream);
                if (size != default(Size))
                {
                    image = image.GetThumbnailImage(size.Width, size.Height, null, IntPtr.Zero);
                }

                using (var memoryStream = new MemoryStream())
                {
                    image.Save(memoryStream, ImageFormat.Png);
                    thumbnail = memoryStream.ToArray();
                }
            }

            return thumbnail;
        }

        /// <summary>
        /// Gets all the slides inside PowerPoint file.
        /// </summary>
        /// <returns>All the slides.</returns>
        public IEnumerable<PowerpointSlide> GetSlides()
        {
            List<PowerpointSlide> slides = new List<PowerpointSlide>();
            int nbSlides = this.SlidesCount();
            for (int i = 0; i < nbSlides; i++)
            {
                slides.Add(this.GetSlide(i));
            }
            return slides;
        }

        /// <summary>
        /// Gets the PowerpointSlide given a slide index.
        /// </summary>
        /// <param name="slideIndex">Index of the slide.</param>
        /// <returns>A PowerpointSlide.</returns>
        public PowerpointSlide GetSlide(int slideIndex)
        {
            PresentationPart presentationPart = this.presentationDocument.PresentationPart;

            // Get the collection of slide IDs
            OpenXmlElementList slideIds = presentationPart.Presentation.SlideIdList.ChildElements;

            // Get the relationship ID of the slide
            string relId = ((SlideId)slideIds[slideIndex]).RelationshipId;

            // Get the specified slide part from the relationship ID
            SlidePart slidePart = (SlidePart)presentationPart.GetPartById(relId);

            return new PowerpointSlide(presentationPart, slidePart);
        }

        /// <summary>
        /// Replaces the cells from a table (tbl).
        /// Algorithm for a slide template containing one table.
        /// </summary>
        public static IEnumerable<PowerpointSlide> ReplaceTable_One(PowerpointSlide slideTemplate, PowerpointTable tableTemplate, IList<PowerpointTable.Cell[]> rows)
        {
            return ReplaceTable_Multiple(slideTemplate, tableTemplate, rows, new List<PowerpointSlide>());
        }

        /// <summary>
        /// Replaces the cells from a table (tbl).
        /// Algorithm for a slide template containing multiple tables.
        /// </summary>
        /// <param name="slideTemplate">The slide template that contains the table(s).</param>
        /// <param name="tableTemplate">The table (tbl) to use, should be inside the slide template.</param>
        /// <param name="rows">The rows to replace the table's cells.</param>
        /// <param name="existingSlides">Existing slides created for the other tables inside the slide template.</param>
        /// <returns>The newly created slides if any.</returns>
        public static IEnumerable<PowerpointSlide> ReplaceTable_Multiple(PowerpointSlide slideTemplate, PowerpointTable tableTemplate, IList<PowerpointTable.Cell[]> rows, List<PowerpointSlide> existingSlides)
        {
            List<PowerpointSlide> slidesCreated = new List<PowerpointSlide>();

            string tag = tableTemplate.Title;

            PowerpointSlide lastSlide = slideTemplate;
            if (existingSlides.Count > 0)
            {
                lastSlide = existingSlides.Last();
            }

            PowerpointSlide lastSlideTemplate = lastSlide.Clone();

            foreach (PowerpointSlide slide in existingSlides)
            {
                PowerpointTable table = slide.FindTables(tag).First();
                List<PowerpointTable.Cell[]> remainingRows = table.SetRows(rows);
                rows = remainingRows;
            }

            // Force SetRows() at least once if there is no existingSlides
            // this means we are being called by ReplaceTable_One()
            bool loopOnce = existingSlides.Count == 0;

            while (loopOnce || rows.Count > 0)
            {
                PowerpointSlide newSlide = lastSlideTemplate.Clone();
                PowerpointTable table = newSlide.FindTables(tag).First();
                List<PowerpointTable.Cell[]> remainingRows = table.SetRows(rows);
                rows = remainingRows;

                PowerpointSlide.InsertAfter(newSlide, lastSlide);
                lastSlide = newSlide;
                slidesCreated.Add(newSlide);

                if (loopOnce) loopOnce = false;
            }

            lastSlideTemplate.Remove();

            return slidesCreated;
        }
    }
}
