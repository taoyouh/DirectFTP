using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FtpExplorer
{
    class History<T> : IEnumerable<T>, INotifyPropertyChanged
    {
        object dataLock = new object();
        LinkedList<T> data = new LinkedList<T>();
        LinkedListNode<T> currentNode = null;

        public T Current {
            get
            {
                if (currentNode != null)
                    return currentNode.Value;
                else
                    return default(T);
            }
        }

        public bool IsEmpty => data.First == null;

        public void Navigate(T item)
        {
            lock (dataLock)
            {
                if (currentNode == null)
                {
                    currentNode = data.AddLast(item);
                }
                else
                {
                    currentNode = data.AddAfter(currentNode, item);
                    if (currentNode.Previous?.Previous == null)
                    {
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoBack)));
                    }
                    if (currentNode.Next != null)
                    {
                        while (currentNode.Next != null)
                            data.Remove(currentNode.Next);
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoForward)));
                    }
                }
            }
        }

        public bool CanGoForward => currentNode?.Next != null;

        public T GoForward()
        {
            lock (dataLock)
            {
                if (currentNode.Next == null)
                    throw new InvalidOperationException();
                currentNode = currentNode.Next;
                if (currentNode.Next == null)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoForward)));
                if (currentNode.Previous?.Previous == null)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoBack)));
                return currentNode.Value;
            }
        }

        public bool CanGoBack => currentNode?.Previous != null;

        public T GoBack()
        {
            lock (dataLock)
            {
                if (currentNode.Previous == null)
                    throw new InvalidOperationException();
                currentNode = currentNode.Previous;
                if (currentNode.Previous == null)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoBack)));
                if (currentNode.Next?.Next == null)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoForward)));
                return currentNode.Value;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        struct Enumerator : IEnumerator<T>
        {
            History<T> data;
            LinkedListNode<T> nextNode;

            internal Enumerator(History<T> source)
            {
                data = source;
                nextNode = data.data.First;
            }

            public T Current => nextNode.Previous.Value;

            object IEnumerator.Current => nextNode.Previous.Value;

            public void Dispose()
            { }

            public bool MoveNext()
            {
                nextNode = nextNode.Next;
                return (nextNode?.Previous != null);
            }

            public void Reset()
            {
                nextNode = data.data.First;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new Enumerator(this);
        }
    }

}
