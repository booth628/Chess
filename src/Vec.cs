using System.ComponentModel;

namespace Chess
{
    public class Vec
    {
        public readonly byte[] Items;
        public int Count = 0;
        public Vec (int maxSize)
        {
            Items = new byte[maxSize];
        }
        public void Add (byte data)
        {
            Items[Count] = data;
            Count++;
        }
        public void Remove (byte data)
        {
            if (Count == 0) return;
            for (int i = 0; i < Count; i++)
            {
                if (data == Items[i])
                {
                    Items[i] = Items[Count - 1];
                    Count--;
                }
            }
        }
        public byte this[int index]
        {
            get { return Items[index]; }
            set { Items[index] = value; }
        }
    }
}