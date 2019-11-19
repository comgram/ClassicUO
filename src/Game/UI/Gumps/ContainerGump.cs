#region license

//  Copyright (C) 2019 ClassicUO Development Community on Github
//
//	This project is an alternative client for the game Ultima Online.
//	The goal of this is to develop a lightweight client considering 
//	new technologies.  
//      
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System.Collections.Generic;
using System.IO;
using System.Linq;

using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.IO;
using ClassicUO.Renderer;

using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    internal class ContainerGump : MinimizableGump
    {
        private GumpPic _iconized;
        internal override GumpPic Iconized => _iconized;
        private HitBox _iconizerArea;
        internal override HitBox IconizerArea => _iconizerArea;
        private long _corpseEyeTicks;
        private bool _hideIfEmpty;
        private ContainerData _data;
        private int _eyeCorspeOffset;
        private GumpPic _eyeGumpPic;
        private bool _isCorspeContainer;
        private Grid _grid;

        public ContainerGump() : base(0, 0)
        {
        }

        public ContainerGump(Serial serial, Graphic gumpid) : this()
        {
            LocalSerial = serial;
            Item item = World.Items.Get(serial);

            if (item == null)
            {
                Dispose();
                return;
            }

            Graphic = gumpid;

            BuildGump();

            foreach (var c in Children.OfType<ItemGump>())
                c.Dispose();

            foreach (Item i in item.Items.Where(s => s != null && s.IsLootable))
                //FIXME: this should be disabled. Server sends the right position
                //CheckItemPosition(i);
                Add(new ItemGump(i));
        }

        public Graphic Graphic { get; }

        public TextContainer TextContainer { get; } = new TextContainer();

        private void BuildGump()
        {
            CanMove = true;
            CanBeSaved = true;
            WantUpdateSize = false;
            _isCorspeContainer = Graphic == 0x0009;

            Item item = World.Items.Get(LocalSerial);

            if (item == null)
            {
                Dispose();
                return;
            }

            item.Items.Added -= ItemsOnAdded;
            item.Items.Removed -= ItemsOnRemoved;
            item.Items.Added += ItemsOnAdded;
            item.Items.Removed += ItemsOnRemoved;

            float scale = UIManager.ContainerScale;

            _data = ContainerManager.Get(Graphic);
            if(_data.MinimizerArea != Rectangle.Empty && _data.IconizedGraphic != 0)
            {
                _iconizerArea = new HitBox((int) (_data.MinimizerArea.X* scale), 
                                           (int) (_data.MinimizerArea.Y * scale),
                                           (int) (_data.MinimizerArea.Width * scale),
                                           (int) (_data.MinimizerArea.Height * scale));
                _iconized = new GumpPic(0, 0, _data.IconizedGraphic, 0);
            }
            Graphic g = _data.Graphic;

            GumpPicContainer container;
            Add(container = new GumpPicContainer(0, 0, g, 0, item));

            if (_isCorspeContainer)
            {
                if (World.Player.ManualOpenedCorpses.Contains(LocalSerial))
                    World.Player.ManualOpenedCorpses.Remove(LocalSerial);
                else if(World.Player.AutoOpenedCorpses.Contains(LocalSerial) &&
                ProfileManager.Current != null && ProfileManager.Current.SkipEmptyCorpse)
                {
                    IsVisible = false;
                    _hideIfEmpty = true;
                }

                _eyeGumpPic?.Dispose();
                Add(_eyeGumpPic = new GumpPic((int) (45 * scale), (int) (30 * scale), 0x0045, 0));

                _eyeGumpPic.Width = (int)(_eyeGumpPic.Width * scale);
                _eyeGumpPic.Height = (int)(_eyeGumpPic.Height * scale);
            }


            Width = container.Width = (int)(container.Width * scale);
            Height = container.Height = (int) (container.Height * scale);

            if (ProfileManager.Current.UseGridContainers)
            {
                int sX = (int)(_data.Bounds.X * scale);
                int sY = (int)(_data.Bounds.Y * scale);
                int sW = (int)(_data.Bounds.Width * scale);
                int sH = (int)(_data.Bounds.Height * scale);
                int itemSize = (sW - sX - 14) / 6;
                Add(_grid = new Grid(sX, sY, sW - sX, sH - sY, itemSize, 6, 10));
            }

            ContainerGump gg = UIManager.Gumps.OfType<ContainerGump>().FirstOrDefault(s => s.LocalSerial == LocalSerial);

            if (gg == null)
            {
                if (UIManager.GetGumpCachePosition(LocalSerial, out Point location) && item.Serial == World.Player.Equipment[(int) Layer.Backpack])
                    Location = location;
                else
                {
                    if (ProfileManager.Current.OverrideContainerLocation)
                    {
                        switch (ProfileManager.Current.OverrideContainerLocationSetting)
                        {
                            case 0:
                                SetPositionNearGameObject(g, item);
                                break;
                            case 1:
                                SetPositionTopRight();
                                break;
                            case 2:
                                SetPositionByLastDragged();
                                break;
                        }

                        if ((X + Width) > CUOEnviroment.Client.Window.ClientBounds.Width)
                        {
                            X -= Width;
                        }

                        if ((Y + Height) > CUOEnviroment.Client.Window.ClientBounds.Height)
                        {
                            Y -= Height;
                        }
                    }
                    else
                    {
                        ContainerManager.CalculateContainerPosition(g);
                        X = ContainerManager.X;
                        Y = ContainerManager.Y;
                    }
                }
            }
            else
            {
                X = gg.X;
                Y = gg.Y;
            }


            if (_data.OpenSound != 0)
                CUOEnviroment.Client.Scene.Audio.PlaySound(_data.OpenSound);
        }

        public override void Update(double totalMS, double frameMS)
        {
            base.Update(totalMS, frameMS);

            Item item = World.Items.Get(LocalSerial);

            if (item == null || item.IsDestroyed)
            {
                Dispose();
                return;
            }

            if (IsDisposed) return;

            if (_isCorspeContainer && _corpseEyeTicks < totalMS)
            {
                _eyeCorspeOffset = _eyeCorspeOffset == 0 ? 1 : 0;
                _corpseEyeTicks = (long) totalMS + 750;
                _eyeGumpPic.Graphic = (Graphic) (0x0045 + _eyeCorspeOffset);
                float scale = UIManager.ContainerScale;
                _eyeGumpPic.Width = (int)(_eyeGumpPic.Texture.Width * scale);
                _eyeGumpPic.Height = (int)(_eyeGumpPic.Texture.Height * scale);
            }
            if(Iconized != null) Iconized.Hue = item.Hue;
        }

        public void ForceUpdate()
        {
            Children[0].Dispose();
            _iconizerArea?.Dispose();
            _iconized?.Dispose();
            _grid?.Dispose();
            BuildGump();

            ItemsOnAdded(null, new CollectionChangedEventArgs<Serial>(FindControls<ItemGump>().Select(s => s.LocalSerial)));
        }

        public override void Save(BinaryWriter writer)
        {
            base.Save(writer);
            writer.Write(LocalSerial);
            writer.Write(Graphic);
        }

        public override void Restore(BinaryReader reader)
        {
            base.Restore(reader);


            LocalSerial = reader.ReadUInt32();
            CUOEnviroment.Client.GetScene<GameScene>()?.DoubleClickDelayed(LocalSerial);
            reader.ReadUInt16();

            Dispose();
        }

        private void ItemsOnRemoved(object sender, CollectionChangedEventArgs<Serial> e)
        {
            RemoveItemsInside(e);
        }

        private void ItemsOnAdded(object sender, CollectionChangedEventArgs<Serial> e)
        {
            RemoveItemsInside(e);

            foreach (Serial s in e)
            {
                var item = World.Items.Get(s);

                if (item == null || !item.IsLootable)
                    continue;


                var itemControl = new ItemGump(item);
                CheckItemControlPosition(itemControl, item);

                if (ProfileManager.Current != null && ProfileManager.Current.ScaleItemsInsideContainers)
                {
                    float scale = UIManager.ContainerScale;

                    itemControl.Width = (int)(itemControl.Width * scale);
                    itemControl.Height = (int)(itemControl.Height * scale);
                }

                Add(itemControl);


                if (ProfileManager.Current != null && ProfileManager.Current.UseGridContainers)
                {
                    _grid.SetItem(item);
                    itemControl.IsVisible = false;
                }

                if (_hideIfEmpty && !IsVisible)
                    IsVisible = true;
            }
        }

        private void RemoveItemsInside(IEnumerable<Serial> e)
        {
            if (ProfileManager.Current != null && ProfileManager.Current.UseGridContainers)
            {
                foreach (var v in _grid?.Children.OfType<GridItem>()
                    .Where(s => s.HasItem && e.Contains(s.Item.Serial)))
                    v.UnsetItem();
            }

            foreach (ItemGump v in Children.OfType<ItemGump>().Where(s => e.Contains(s.LocalSerial)))
                v.Dispose();
        }


        private void CheckItemControlPosition(ItemGump itemGump, Item item)
        {         
            float scale = UIManager.ContainerScale;

            int x = (int) (itemGump.X * scale);
            int y = (int) (itemGump.Y * scale);
          
            ArtTexture texture = FileManager.Art.GetTexture(item.DisplayedGraphic);

            int boundX = (int)(_data.Bounds.X * scale);
            int boundY = (int)(_data.Bounds.Y * scale);

            if (texture != null && !texture.IsDisposed)
            {
                int boundW = (int)(_data.Bounds.Width * scale);
                int boundH = (int)(_data.Bounds.Height * scale);

                int textureW, textureH;

                if (ProfileManager.Current != null && ProfileManager.Current.ScaleItemsInsideContainers)
                {
                    textureW = (int)(texture.Width * scale);
                    textureH = (int)(texture.Height * scale);
                }
                else
                {
                    textureW = texture.Width;
                    textureH = texture.Height;
                }

                if (x < boundX)
                    x = boundX;

                if (y < boundY)
                    y = boundY;


                if (x + textureW > boundW)
                    x = boundW - textureW;

                if (y + textureH > boundH)
                    y = boundH - textureH;
            }
            else
            {
                x = boundX;
                y = boundY;
            }

            if (x < 0)
                x = 0;

            if (y < 0)
                y = 0;


            if (x != itemGump.X || y != itemGump.Y)
            {
                itemGump.X = x;
                itemGump.Y = y;
            }
        }

        private void SetPositionNearGameObject(Graphic g, Item item)
        {
            if (World.Player.Equipment[(int)Layer.Bank] != null && item.Serial == World.Player.Equipment[(int)Layer.Bank])
            {
                // open bank near player
                X = World.Player.RealScreenPosition.X + ProfileManager.Current.GameWindowPosition.X + 40;
                Y = World.Player.RealScreenPosition.Y + ProfileManager.Current.GameWindowPosition.Y - (Height >> 1);
            }
            else if (item.OnGround)
            {
                // item is in world
                X = item.RealScreenPosition.X + ProfileManager.Current.GameWindowPosition.X + 40;
                Y = item.RealScreenPosition.Y + ProfileManager.Current.GameWindowPosition.Y - (Height >> 1);
            }
            else if (item.Container.IsMobile)
            {
                // pack animal, snooped player, npc vendor
                Mobile mobile = World.Mobiles.Get(item.Container);
                if (mobile != null)
                {
                    X = mobile.RealScreenPosition.X + ProfileManager.Current.GameWindowPosition.X + 40;
                    Y = mobile.RealScreenPosition.Y + ProfileManager.Current.GameWindowPosition.Y - (Height >> 1);
                }
            }
            else
            {
                // in a container, open near the container
                ContainerGump parentContainer = UIManager.Gumps.OfType<ContainerGump>().FirstOrDefault(s => s.LocalSerial == item.Container);
                if (parentContainer != null)
                {
                    X = parentContainer.X + (Width >> 1);
                    Y = parentContainer.Y;
                }
                else
                {
                    // I don't think we ever get here?
                    ContainerManager.CalculateContainerPosition(g);
                    X = ContainerManager.X;
                    Y = ContainerManager.Y;
                }
            }
        }

        private void SetPositionTopRight()
        {
            X = CUOEnviroment.Client.Window.ClientBounds.Width - Width;
            Y = 0;
        }

        private void SetPositionByLastDragged()
        {
            X = ProfileManager.Current.OverrideContainerLocationPosition.X - (Width >> 1);
            Y = ProfileManager.Current.OverrideContainerLocationPosition.Y - (Height >> 1);
        }

        public override void Dispose()
        {
            TextContainer.Clear();

            Item item = World.Items.Get(LocalSerial);

            if (item != null)
            {
                item.Items.Added -= ItemsOnAdded;
                item.Items.Removed -= ItemsOnRemoved;

                if (World.Player != null && item == World.Player.Equipment[(int) Layer.Backpack]) UIManager.SavePosition(item, Location);

                foreach (Item child in item.Items)
                {
                    if (child.Container == item)
                        UIManager.GetGump<ContainerGump>(child)?.Dispose();
                }

                if (_data.ClosedSound != 0)
                    CUOEnviroment.Client.Scene.Audio.PlaySound(_data.ClosedSound);
            }

            base.Dispose();
        }

        protected override void OnDragEnd(int x, int y)
        {
            if (ProfileManager.Current.OverrideContainerLocation && ProfileManager.Current.OverrideContainerLocationSetting == 2)
            {
                Point gumpCenter = new Point(X + (Width >> 1), Y + (Height >> 1));
                ProfileManager.Current.OverrideContainerLocationPosition = gumpCenter;
            }

            base.OnDragEnd(x, y);
        }


        class Grid : Control
        {
            private readonly GridItem[] _gridList;
            private ScrollBar _scrollBar;
            private readonly int _itemSize, _rows, _columns;
            private readonly int _scrollbarHeight;

            public Grid(int x, int y, int width, int height, int itemSize, int rows, int columns)
            {
                CanMove = true;
                AcceptMouseInput = true;
                X = x;
                Y = y;
                WantUpdateSize = false;

                //_dataBox = new DataBox(0,0, Width, Height);
                //Add(_dataBox);
                _itemSize = itemSize;

                _columns = columns;
                _rows = rows;

                _gridList = new GridItem[columns * rows];

                int visible_rows = width / itemSize;
                int visible_columns = height / itemSize;

                //for (int col = 0; col < columns; col++)
                //{
                //    int yy = col * itemSize;
                //    for (int row = 0; row < rows; row++)
                //    {
                //        Add(_gridList[col * rows + row] = new GridItem(row * itemSize, yy, itemSize - 2, itemSize - 2));
                //    }
                //} 
                
                Width = visible_rows * itemSize;
                Height = visible_columns * itemSize;

                int row = 0;
                int col = 0;

                for (int i = 0; i < _gridList.Length; i++)
                {
                    Add(_gridList[col * _rows + row] = new GridItem(row * itemSize, col * itemSize, itemSize - 2, itemSize - 2));

                    row++;

                    if (row >= _rows)
                    {
                        row = 0;
                        col++;
                    }
                }

                _scrollBar = new ScrollBar(Width, 0, height);
                _scrollBar.MinValue = 0;
                _scrollBar.MaxValue = height;
                _scrollbarHeight = -1;

                Width += 14;
                Add(_scrollBar);
            }


            public void SetItem(Serial serial)
            {
                for (int i = 0; i < _gridList.Length; i++)
                {
                    if (!_gridList[i].HasItem)
                    {
                        _gridList[i].SetItem(serial);
                        break;
                    }
                }
            }

            public void SetItem(int position, Serial serial)
            {
                if (position >= 0 && position < _gridList.Length)
                {
                    _gridList[position].SetItem(serial);
                }
            }

            public override void Update(double totalMS, double frameMS)
            {
                base.Update(totalMS, frameMS);
                CalculateScrollBarMaxValue();
                _scrollBar.IsVisible = _scrollBar.MaxValue > _scrollBar.MinValue;
            }

            protected override void OnMouseWheel(MouseEvent delta)
            {
                switch (delta)
                {
                    case MouseEvent.WheelScrollUp:
                        _scrollBar.Value -= _scrollBar.ScrollStep;

                        break;

                    case MouseEvent.WheelScrollDown:
                        _scrollBar.Value += _scrollBar.ScrollStep;

                        break;
                }
            }

            public override bool Draw(UltimaBatcher2D batcher, int x, int y)
            {
                ResetHueVector();
                _hueVector.Z = 0.5f;
                batcher.Draw2D(Textures.GetTexture(Color.Black), x, y, Width, Height, ref _hueVector);


                _scrollBar.Draw(batcher, x + _scrollBar.X, y + _scrollBar.Y);

                Rectangle scissor = ScissorStack.CalculateScissors(Matrix.Identity, x, y, Width, Height);

                if (ScissorStack.PushScissors(scissor))
                {
                    batcher.EnableScissorTest(true);

                    int height = 0;
                    for (int col = 0; col < _columns; col++)
                    {                   
                        for (int row = 0; row < _rows; row++)
                        {
                            var item = _gridList[col * _rows + row];

                            item.Y = height - _scrollBar.Value;

                            if (height + item.Height <= _scrollBar.Value)
                            {

                            }
                            else
                            {
                                item.Draw(batcher, x + item.X, y + item.Y);
                            }
                        
                        }

                        height += _itemSize;
                    }


                    batcher.EnableScissorTest(false);
                    ScissorStack.PopScissors();
                }


                return true;
            }


            private void CalculateScrollBarMaxValue()
            {
                _scrollBar.Height = _scrollbarHeight >= 0 ? _scrollbarHeight : Height;
                bool maxValue = _scrollBar.Value == _scrollBar.MaxValue && _scrollBar.MaxValue != 0;
                int height = _columns * (_itemSize);
                height -= _scrollBar.Height;


                if (height > 0)
                {
                    _scrollBar.MaxValue = height;

                    if (maxValue)
                        _scrollBar.Value = _scrollBar.MaxValue;
                }
                else
                {
                    _scrollBar.MaxValue = 0;
                    _scrollBar.Value = 0;
                }
            }
        }

        class GridItem : Control
        {
            private readonly TextureControl _textureControl;

            public GridItem(int x, int y, int width, int height)
            {
                CanMove = true;
                AcceptMouseInput = true;
                X = x;
                Y = y;
                Width = width;
                Height = height;
                WantUpdateSize = false;

                _textureControl = new TextureControl() 
                { 
                    ScaleTexture = true,
                    Width = width,
                    Height = height
                };

                Add(_textureControl);
            }

            public Item Item { get; private set; }
            public bool HasItem => Item != null && !Item.IsDestroyed;


            public void SetItem(Serial serial)
            {
                if (serial.IsValid)
                {
                    Item = World.Items.Get(serial);
                    _textureControl.Texture = FileManager.Art.GetTexture(Item.DisplayedGraphic);
                }
                else
                {
                    UnsetItem();
                }
            }

            public void UnsetItem()
            {
                Item = null;
                _textureControl.Texture = null;
            }         

            public override bool Draw(UltimaBatcher2D batcher, int x, int y)
            {
                ResetHueVector();

                base.Draw(batcher, x, y);
                batcher.DrawRectangle(Textures.GetTexture(MouseIsOver || _textureControl.MouseIsOver ? Color.LimeGreen : Color.Gray), x, y, Width, Height, ref _hueVector);
                return true;
            }
        }
    }
}