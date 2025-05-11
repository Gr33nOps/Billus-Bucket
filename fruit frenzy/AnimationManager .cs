using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace fruit_frenzy
{
    internal class AnimationManager
    {
        int numFrames;
        int numColumn;
        Vector2 size;

        int counter;
        int activeFrame;
        float elapsedTime;
        float interval;

        int rowPos;
        int colPos;

        public AnimationManager(int numFrames, int numColumn, Vector2 Size, float interval)
        {
            this.numFrames = numFrames;
            this.numColumn = numColumn;
            this.size = Size;
            this.interval = interval;
            counter = 0;
            activeFrame = 0;
            elapsedTime = 0f;
            rowPos = 0; 
            colPos = 0;
        }

        public void Update(GameTime gameTime)
        {
            elapsedTime += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (elapsedTime > interval)
            {
                elapsedTime = 0f;
                NextFrame();
            }
        }

        private void NextFrame() 
        {
            activeFrame++;
            colPos++;
            if (activeFrame >= numFrames)
            {
                resetAnimation();
            }   
            if(colPos >= numColumn) 
            {
                colPos = 0;
                rowPos++;
            }

        }

        private void resetAnimation() 
        {
            activeFrame = 0;
            colPos = 0;
            rowPos = 0;
        }

        public Rectangle GetFrame()
        {
            return new Rectangle(
                colPos * (int)size.X,
                rowPos * (int)size.Y,
                (int)size.X,
                (int)size.Y);   
        }
    }
}
