using RecursiveAI.OneDocument.Documents;

namespace RecursiveAI.OneDocument.Layout;

public static partial class LayoutEngine
{
    private sealed partial class Flow
    {
        // The rows that go on one page, collected before they are turned into nodes.
        private sealed class Part
        {
            public readonly List<SliceRow> Rows = [];
            public readonly List<SliceCell> Cells = [];
            public double Height;
            public int BodyRows; // rows that are not repeated headers

            public void Add(SliceRow row, double spacing)
            {
                Height += row.Height + spacing + (Rows.Count == 0 ? spacing : 0);
                Rows.Add(row);
                if (!row.Repeated)
                {
                    BodyRows++;
                }
            }
        }

        private void PlaceTable(FormattedTable ft)
        {
            if (ft.Rows.Count == 0)
            {
                return;
            }

            if (NeedsPage)
            {
                StartPage();
                if (Stopped)
                {
                    return;
                }
            }

            if (_forceBreakBeforeNext)
            {
                _forceBreakBeforeNext = false;
                if (!_pageHasContent)
                {
                    // Nothing follows a break on an empty page or column: no blank one is made.
                }
                else if (_forceIsColumn)
                {
                    NewPage(honourSpace: true);
                }
                else
                {
                    ForcePage(honourSpace: true);
                }
            }

            if (ft.Floating)
            {
                PlaceFloatingTable(ft);
                return;
            }

            if (_pageHasContent)
            {
                _y += _pendingAfter;
            }

            _pendingAfter = 0;
            _previous = null;

            var part = new Part();
            int partIndex = 0;
            int r = 0;
            int breakEnd = -1; // while inside a vertically merged group that had to be broken, the group's last row
            double s = ft.Spacing;

            void Flush(bool continuesOn)
            {
                if (part.Rows.Count == 0)
                {
                    return;
                }

                CloseDeferred(part);
                LayoutPage page = _page!;
                LayoutTable node = TableEmitter.Emit(ft, part.Rows, part.Cells, _colX + ft.Left, _y);
                node.ContinuesFromPreviousPage = partIndex > 0;
                node.ContinuesOnNextPage = continuesOn;
                page.Add(node);
                _y += part.Height;
                _regionMaxY = Math.Max(_regionMaxY, _y);
                _pageHasContent = true;
                _honourSpaceBefore = true;
                part = new Part();
                partIndex++;
            }

            while (r < ft.Rows.Count && !Stopped)
            {
                if (part.Rows.Count == 0 && partIndex > 0 && ft.HeaderCount > 0 && r >= ft.HeaderCount)
                {
                    AddRepeatedHeaders(ft, part);
                }

                double pageBottom = _limitBottom;
                double avail = pageBottom - (_y + part.Height);
                bool pageOccupied = _pageHasContent || part.BodyRows > 0;
                int e = breakEnd >= r ? breakEnd : ft.GroupEnd[r];
                bool inBrokenGroup = breakEnd >= r;
                bool fresh = !inBrokenGroup && Fresh(ft, r, e);
                double groupHeight = 0;
                for (int i = r; i <= e; i++)
                {
                    groupHeight += ft.Rows[i].Height + s;
                }

                if (part.Rows.Count == 0)
                {
                    groupHeight += s;
                }

                if (fresh && groupHeight <= avail + 0.01)
                {
                    AddFull(ft, part, r, e);
                    r = e + 1;
                    continue;
                }

                double pageHeight = _limitBottom - _regionTop;
                if (e == r && !inBrokenGroup)
                {
                    FormattedRow row = ft.Rows[r];
                    bool keep = row.CantSplit || row.Rule == "exact";
                    if (keep && fresh && pageOccupied && row.Height + (2 * s) <= pageHeight)
                    {
                        Flush(continuesOn: true);
                        NewPage(honourSpace: false);
                        continue;
                    }

                    if (row.Rule == "exact" && fresh)
                    {
                        // An exact row is cut to its height even when that is taller than the page.
                        AddFull(ft, part, r, r);
                        r++;
                        continue;
                    }

                    if (row.Starting.All(static c => c.Cursor >= c.Units.Count))
                    {
                        // Nothing left to split (empty cells): the row is placed whole, or moves to the next page.
                        if (pageOccupied && row.Height + (2 * s) <= pageHeight)
                        {
                            Flush(continuesOn: true);
                            NewPage(honourSpace: false);
                            continue;
                        }

                        AddFull(ft, part, r, r);
                        r++;
                        continue;
                    }

                    (bool placed, bool done) = SplitRow(ft, part, r, avail, force: !pageOccupied);
                    if (!placed)
                    {
                        Flush(continuesOn: true);
                        NewPage(honourSpace: false);
                        continue;
                    }

                    if (done)
                    {
                        r++;
                    }
                    else
                    {
                        Flush(continuesOn: true);
                        NewPage(honourSpace: false);
                    }

                    continue;
                }

                // A group of rows joined by vertical merges that does not fit.
                if (fresh && pageOccupied && groupHeight <= pageHeight)
                {
                    Flush(continuesOn: true);
                    NewPage(honourSpace: false);
                    continue;
                }

                breakEnd = e;
                double room = avail;
                bool addedAny = false;
                while (r <= e)
                {
                    double rh = ft.Rows[r].Height + s + (part.Rows.Count == 0 ? s : 0);
                    if (rh > room + 0.01 && (addedAny || pageOccupied))
                    {
                        break;
                    }

                    AddGroupRow(ft, part, r);
                    room -= rh;
                    addedAny = true;
                    r++;
                }

                if (r > e)
                {
                    breakEnd = -1;
                }
                else
                {
                    Flush(continuesOn: true);
                    NewPage(honourSpace: false);
                }
            }

            Flush(continuesOn: false);
        }

        private static bool Fresh(FormattedTable ft, int r0, int r1)
        {
            foreach (FormattedCell c in ft.Cells)
            {
                if (c.Row >= r0 && c.Row <= r1 && c.Cursor != 0)
                {
                    return false;
                }
            }

            return true;
        }

        // Header rows repeat at the top of each later page when they leave room for body rows (ECMA-376 Part 1 §17.4.49, tblHeader).
        private void AddRepeatedHeaders(FormattedTable ft, Part part)
        {
            double h = ft.Spacing;
            for (int i = 0; i < ft.HeaderCount; i++)
            {
                h += ft.Rows[i].Height + ft.Spacing;
            }

            if (h > (_limitBottom - _regionTop) * 0.5)
            {
                return;
            }

            for (int i = 0; i < ft.HeaderCount; i++)
            {
                part.Add(new SliceRow { Row = i, Height = ft.Rows[i].Height, Repeated = true }, ft.Spacing);
            }

            foreach (FormattedCell c in ft.Cells)
            {
                if (c.Row < ft.HeaderCount)
                {
                    part.Cells.Add(new SliceCell { Cell = c, U0 = 0, U1 = c.Units.Count, FirstRow = c.Row, LastRow = Math.Min(ft.HeaderCount - 1, c.Row + c.RowSpan - 1) });
                }
            }
        }

        // Rows r0 to r1 whole, every cell complete. An exact row cuts its content at its height.
        private static void AddFull(FormattedTable ft, Part part, int r0, int r1)
        {
            int first = part.Rows.Count;
            for (int r = r0; r <= r1; r++)
            {
                part.Add(new SliceRow { Row = r, Height = ft.Rows[r].Height }, ft.Spacing);
            }

            foreach (FormattedCell c in ft.Cells)
            {
                if (c.Row < r0 || c.Row > r1)
                {
                    continue;
                }

                int u1 = c.Units.Count;
                FormattedRow row = ft.Rows[c.Row];
                if (row.Rule == "exact" && c.RowSpan == 1 && c.Need > row.Height + 0.01)
                {
                    u1 = FitUnits(c, 0, row.Height - c.PadTop - c.PadBottom);
                }

                c.Cursor = c.Units.Count;
                part.Cells.Add(new SliceCell { Cell = c, U0 = 0, U1 = u1, FirstRow = first + (c.Row - r0), LastRow = first + (Math.Min(r1, c.Row + c.RowSpan - 1) - r0) });
            }
        }

        // One row of a merged group that is being broken across pages; its merged cells are sized when the part closes.
        private static void AddGroupRow(FormattedTable ft, Part part, int r)
        {
            int index = part.Rows.Count;
            part.Add(new SliceRow { Row = r, Height = ft.Rows[r].Height }, ft.Spacing);
            foreach (FormattedCell c in ft.Cells)
            {
                if (c.Row > r || c.Row + c.RowSpan - 1 < r)
                {
                    continue;
                }

                SliceCell? existing = part.Cells.FirstOrDefault(sc => sc.Cell == c);
                if (existing is not null)
                {
                    existing.LastRow = index;
                    continue;
                }

                part.Cells.Add(new SliceCell { Cell = c, U0 = c.Cursor, U1 = -1, FirstRow = index, LastRow = index });
            }
        }

        private static void CloseDeferred(Part part)
        {
            foreach (SliceCell sc in part.Cells)
            {
                if (sc.U1 >= 0)
                {
                    continue;
                }

                FormattedCell c = sc.Cell;
                bool regionEnds = c.Row + c.RowSpan - 1 <= part.Rows[sc.LastRow].Row;
                if (regionEnds)
                {
                    sc.U1 = c.Units.Count;
                }
                else
                {
                    double area = 0;
                    for (int i = sc.FirstRow; i <= sc.LastRow; i++)
                    {
                        area += part.Rows[i].Height;
                    }

                    sc.U1 = Math.Max(sc.U0, FitUnits(c, sc.U0, area - c.PadTop - c.PadBottom) );
                }

                c.Cursor = sc.U1;
            }
        }

        // The end of the units that fit in a content height, counted from u0; ends at Units.Count.
        private static int FitUnits(FormattedCell c, int u0, double height)
        {
            int u = u0;
            while (u < c.Units.Count && c.Extent(u0, u + 1) <= height + 0.01)
            {
                u++;
            }

            return u;
        }

        // Splits the single row r across pages. Each cell takes the lines (or nested rows) that fit; returns whether anything was placed
        // and whether the row is complete. With force, a part always holds at least one unit of every cell that has content left.
        private static (bool Placed, bool Done) SplitRow(FormattedTable ft, Part part, int r, double avail, bool force)
        {
            double s = ft.Spacing;
            double room = avail - s - (part.Rows.Count == 0 ? s : 0);
            FormattedRow row = ft.Rows[r];
            var ends = new int[row.Starting.Count];
            bool any = false;
            bool anyBefore = false;
            for (int i = 0; i < row.Starting.Count; i++)
            {
                FormattedCell c = row.Starting[i];
                anyBefore |= c.Cursor > 0;
                if (c.Cursor >= c.Units.Count)
                {
                    ends[i] = c.Cursor;
                    continue;
                }

                ends[i] = FitUnits(c, c.Cursor, room - c.PadTop - c.PadBottom);
                if (ends[i] == c.Cursor && force)
                {
                    ends[i] = c.Cursor + 1;
                }

                any |= ends[i] > c.Cursor;
            }

            if (!any)
            {
                return (false, false);
            }

            double height = 0;
            bool done = true;
            for (int i = 0; i < row.Starting.Count; i++)
            {
                FormattedCell c = row.Starting[i];
                if (ends[i] > c.Cursor)
                {
                    height = Math.Max(height, c.PadTop + c.Extent(c.Cursor, ends[i]) + c.PadBottom);
                }

                done &= ends[i] >= c.Units.Count;
            }

            int index = part.Rows.Count;
            part.Add(new SliceRow { Row = r, Height = height, ContinuesFrom = anyBefore, ContinuesOn = !done }, s);
            for (int i = 0; i < row.Starting.Count; i++)
            {
                FormattedCell c = row.Starting[i];
                part.Cells.Add(new SliceCell { Cell = c, U0 = c.Cursor >= c.Units.Count && c.Units.Count > 0 ? c.Units.Count : c.Cursor, U1 = ends[i], FirstRow = index, LastRow = index });
                c.Cursor = ends[i];
            }

            return (true, done);
        }

        // A floating table is painted at its position and takes no room in the flow; the text that follows wraps around it, keeping the
        // distances tblpPr gives. ECMA-376 Part 1 §17.4.57 (tblpPr).
        private void PlaceFloatingTable(FormattedTable ft)
        {
            LayoutPage page = _page!;
            LayoutRect content = page.ContentArea;
            double anchorX = ft.HorizontalAnchor == "page" ? 0 : content.X;
            double anchorW = ft.HorizontalAnchor == "page" ? page.Width : content.Width;
            double x = ft.HorizontalSpec switch
            {
                "center" => anchorX + ((anchorW - ft.Width) / 2),
                "right" or "outside" => anchorX + anchorW - ft.Width,
                "left" or "inside" => anchorX,
                _ => anchorX + ft.FloatX,
            };
            double anchorY = ft.VerticalAnchor switch { "page" => 0, "text" => _y, _ => content.Y };
            double anchorH = ft.VerticalAnchor == "page" ? page.Height : content.Height;
            double total = ft.Spacing;
            foreach (FormattedRow row in ft.Rows)
            {
                total += row.Height + ft.Spacing;
            }

            double y = ft.VerticalSpec switch
            {
                "center" => anchorY + ((anchorH - total) / 2),
                "bottom" or "outside" => anchorY + anchorH - total,
                "top" or "inside" => anchorY,
                _ => anchorY + ft.FloatY,
            };
            LayoutTable placed = TableEmitter.EmitFull(ft, 0, ft.Rows.Count - 1, x, y);
            page.Add(placed);
            if (_story == LayoutStory.Body)
            {
                page.FloatBoxes.Add(new FloatBox
                {
                    Rect = new LayoutRect(x, y, Math.Max(placed.Bounds.Width, ft.Width), Math.Max(placed.Bounds.Height, total)),
                    Wrap = RecursiveAI.OneDocument.Rendering.Drawing.DrawingWrap.Square,
                    DistanceLeft = ft.DistanceLeft,
                    DistanceRight = ft.DistanceRight,
                    DistanceTop = ft.DistanceTop,
                    DistanceBottom = ft.DistanceBottom,
                });
            }
        }
    }
}
