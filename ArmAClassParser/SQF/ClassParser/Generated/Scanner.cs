
using System;
using System.IO;
using System.Collections;


namespace SQF.ClassParser
{
    public class Token {
        public int kind;    // token kind
        public int pos;     // token position in bytes in the source text (starting at 0)
        public int charPos;  // token position in characters in the source text (starting at 0)
        public int col;     // token column (starting at 1)
        public int line;    // token line (starting at 1)
        public string val;  // token value
        public Token next;  // ML 2005-03-11 Tokens are kept in linked list
    }

    //-----------------------------------------------------------------------------------
    // Buffer
    //-----------------------------------------------------------------------------------
    public class Buffer {
        // This Buffer supports the following cases:
        // 1) seekable stream (file)
        //    a) whole stream in buffer
        //    b) part of stream in buffer
        // 2) non seekable stream (network, console)

        public const int EOF = char.MaxValue + 1;
        const int MIN_BUFFER_LENGTH = 1024; // 1KB
        const int MAX_BUFFER_LENGTH = MIN_BUFFER_LENGTH * 64; // 64KB
        byte[] buf;         // input buffer
        int bufStart;       // position of first byte in buffer relative to input stream
        int bufLen;         // length of buffer
        int fileLen;        // length of input stream (may change if the stream is no file)
        int bufPos;         // current position in buffer
        Stream stream;      // input stream (seekable)
        bool isUserStream;  // was the stream opened by the user?
        
        public Buffer (Stream s, bool isUserStream) {
            stream = s; this.isUserStream = isUserStream;
            
            if (stream.CanSeek) {
                fileLen = (int) stream.Length;
                bufLen = Math.Min(fileLen, MAX_BUFFER_LENGTH);
                bufStart = Int32.MaxValue; // nothing in the buffer so far
            } else {
                fileLen = bufLen = bufStart = 0;
            }

            buf = new byte[(bufLen>0) ? bufLen : MIN_BUFFER_LENGTH];
            if (fileLen > 0) Pos = 0; // setup buffer to position 0 (start)
            else bufPos = 0; // index 0 is already after the file, thus Pos = 0 is invalid
            if (bufLen == fileLen && stream.CanSeek) Close();
        }
        
        protected Buffer(Buffer b) { // called in UTF8Buffer constructor
            buf = b.buf;
            bufStart = b.bufStart;
            bufLen = b.bufLen;
            fileLen = b.fileLen;
            bufPos = b.bufPos;
            stream = b.stream;
            // keep destructor from closing the stream
            b.stream = null;
            isUserStream = b.isUserStream;
        }

        ~Buffer() { Close(); }
        
        protected void Close() {
            if (!isUserStream && stream != null) {
                stream.Close();
                stream = null;
            }
        }
        
        public virtual int Read () {
            if (bufPos < bufLen) {
                return buf[bufPos++];
            } else if (Pos < fileLen) {
                Pos = Pos; // shift buffer start to Pos
                return buf[bufPos++];
            } else if (stream != null && !stream.CanSeek && ReadNextStreamChunk() > 0) {
                return buf[bufPos++];
            } else {
                return EOF;
            }
        }

        public int Peek () {
            int curPos = Pos;
            int ch = Read();
            Pos = curPos;
            return ch;
        }
        
        // beg .. begin, zero-based, inclusive, in byte
        // end .. end, zero-based, exclusive, in byte
        public string GetString (int beg, int end) {
            int len = 0;
            char[] buf = new char[end - beg];
            int oldPos = Pos;
            Pos = beg;
            while (Pos < end) buf[len++] = (char) Read();
            Pos = oldPos;
            return new String(buf, 0, len);
        }

        public int Pos {
            get { return bufPos + bufStart; }
            set {
                if (value >= fileLen && stream != null && !stream.CanSeek) {
                    // Wanted position is after buffer and the stream
                    // is not seek-able e.g. network or console,
                    // thus we have to read the stream manually till
                    // the wanted position is in sight.
                    while (value >= fileLen && ReadNextStreamChunk() > 0);
                }

                if (value < 0 || value > fileLen) {
                    throw new FatalError("buffer out of bounds access, position: " + value);
                }

                if (value >= bufStart && value < bufStart + bufLen) { // already in buffer
                    bufPos = value - bufStart;
                } else if (stream != null) { // must be swapped in
                    stream.Seek(value, SeekOrigin.Begin);
                    bufLen = stream.Read(buf, 0, buf.Length);
                    bufStart = value; bufPos = 0;
                } else {
                    // set the position to the end of the file, Pos will return fileLen.
                    bufPos = fileLen - bufStart;
                }
            }
        }
        
        // Read the next chunk of bytes from the stream, increases the buffer
        // if needed and updates the fields fileLen and bufLen.
        // Returns the number of bytes read.
        private int ReadNextStreamChunk() {
            int free = buf.Length - bufLen;
            if (free == 0) {
                // in the case of a growing input stream
                // we can neither seek in the stream, nor can we
                // foresee the maximum length, thus we must adapt
                // the buffer size on demand.
                byte[] newBuf = new byte[bufLen * 2];
                Array.Copy(buf, newBuf, bufLen);
                buf = newBuf;
                free = bufLen;
            }
            int read = stream.Read(buf, bufLen, free);
            if (read > 0) {
                fileLen = bufLen = (bufLen + read);
                return read;
            }
            // end of stream reached
            return 0;
        }
    }

    //-----------------------------------------------------------------------------------
    // UTF8Buffer
    //-----------------------------------------------------------------------------------
    public class UTF8Buffer: Buffer {
        public UTF8Buffer(Buffer b): base(b) {}

        public override int Read() {
            int ch;
            do {
                ch = base.Read();
                // until we find a utf8 start (0xxxxxxx or 11xxxxxx)
            } while ((ch >= 128) && ((ch & 0xC0) != 0xC0) && (ch != EOF));
            if (ch < 128 || ch == EOF) {
                // nothing to do, first 127 chars are the same in ascii and utf8
                // 0xxxxxxx or end of file character
            } else if ((ch & 0xF0) == 0xF0) {
                // 11110xxx 10xxxxxx 10xxxxxx 10xxxxxx
                int c1 = ch & 0x07; ch = base.Read();
                int c2 = ch & 0x3F; ch = base.Read();
                int c3 = ch & 0x3F; ch = base.Read();
                int c4 = ch & 0x3F;
                ch = (((((c1 << 6) | c2) << 6) | c3) << 6) | c4;
            } else if ((ch & 0xE0) == 0xE0) {
                // 1110xxxx 10xxxxxx 10xxxxxx
                int c1 = ch & 0x0F; ch = base.Read();
                int c2 = ch & 0x3F; ch = base.Read();
                int c3 = ch & 0x3F;
                ch = (((c1 << 6) | c2) << 6) | c3;
            } else if ((ch & 0xC0) == 0xC0) {
                // 110xxxxx 10xxxxxx
                int c1 = ch & 0x1F; ch = base.Read();
                int c2 = ch & 0x3F;
                ch = (c1 << 6) | c2;
            }
            return ch;
        }
    }

    //-----------------------------------------------------------------------------------
    // Scanner
    //-----------------------------------------------------------------------------------
    public class Scanner {
        const char EOL = '\n';
        const int eofSym = 0; /* pdt */
    	const int maxT = 17;
	const int noSym = 17;


        public Buffer buffer; // scanner buffer
        
        Token t;          // current token
        int ch;           // current input character
        int pos;          // byte position of current character
        int charPos;      // position by unicode characters starting with 0
        int col;          // column number of current character
        int line;         // line number of current character
        int oldEols;      // EOLs that appeared in a comment;
        static readonly Hashtable start; // maps first token character to start state

        Token tokens;     // list of tokens already peeked (first token is a dummy)
        Token pt;         // current peek token
        
        char[] tval = new char[128]; // text of current token
        int tlen;         // length of current token
        
        static Scanner() {
            start = new Hashtable(128);
    		for (int i = 49; i <= 57; ++i) start[i] = 2;
		for (int i = 65; i <= 90; ++i) start[i] = 10;
		for (int i = 95; i <= 95; ++i) start[i] = 10;
		for (int i = 97; i <= 122; ++i) start[i] = 10;
		start[45] = 1; 
		start[48] = 11; 
		start[34] = 7; 
		start[36] = 9; 
		start[58] = 14; 
		start[123] = 15; 
		start[125] = 16; 
		start[59] = 17; 
		start[91] = 18; 
		start[93] = 19; 
		start[61] = 20; 
		start[44] = 21; 
		start[Buffer.EOF] = -1;

        }
        public bool FollowedBy(string s)
        {
            Token next = this.Peek();
            return next.val == s;
        }
        public bool FollowedBy(string[] sArr)
        {
            foreach(var s in sArr)
            {
                Token next = this.Peek();
                if(next.val == s)
                    return true;
            }
            return false;
        }
        public bool FollowedByWO(string s)
        {
            Token next = this.Peek();
            return t.val == s || next.val == s;
        }
        public bool FollowedByWO(string[] sArr)
        {
            foreach(var s in sArr)
            {
                Token next = this.Peek();
                if(t.val == s || next.val == s)
                    return true;
            }
            return false;
        }
        public Scanner (string fileName) {
            try {
                Stream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                buffer = new Buffer(stream, false);
                Init();
            } catch (IOException) {
                throw new FatalError("Cannot open file " + fileName);
            }
        }
        
        public Scanner (Stream s) {
            buffer = new Buffer(s, true);
            Init();
        }
        
        void Init() {
            pos = -1; line = 1; col = 0; charPos = -1;
            oldEols = 0;
            NextCh();
            if (ch == 0xEF) { // check optional byte order mark for UTF-8
                NextCh(); int ch1 = ch;
                NextCh(); int ch2 = ch;
                if (ch1 != 0xBB || ch2 != 0xBF) {
                    throw new FatalError(String.Format("illegal byte order mark: EF {0,2:X} {1,2:X}", ch1, ch2));
                }
                buffer = new UTF8Buffer(buffer); col = 0; charPos = -1;
                NextCh();
            }
            pt = tokens = new Token();  // first token is a dummy
        }
        
        void NextCh() {
            if (oldEols > 0) { ch = EOL; oldEols--; } 
            else {
                pos = buffer.Pos;
                // buffer reads unicode chars, if UTF8 has been detected
                ch = buffer.Read(); col++; charPos++;
                // replace isolated '\r' by '\n' in order to make
                // eol handling uniform across Windows, Unix and Mac
                if (ch == '\r' && buffer.Peek() != '\n') ch = EOL;
                if (ch == EOL) { line++; col = 0; }
            }
    
        }

        void AddCh() {
            if (tlen >= tval.Length) {
                char[] newBuf = new char[2 * tval.Length];
                Array.Copy(tval, 0, newBuf, 0, tval.Length);
                tval = newBuf;
            }
            if (ch != Buffer.EOF) {
    			tval[tlen++] = (char) ch;
                NextCh();
            }
        }


    
	bool Comment0() {
		int level = 1, pos0 = pos, line0 = line, col0 = col, charPos0 = charPos;
		NextCh();
			for(;;) {
				if (ch == 10) {
					level--;
					if (level == 0) { oldEols = line - line0; NextCh(); return true; }
					NextCh();
				} else if (ch == Buffer.EOF) return false;
				else NextCh();
			}
	}

	bool Comment1() {
		int level = 1, pos0 = pos, line0 = line, col0 = col, charPos0 = charPos;
		NextCh();
		if (ch == '/') {
			NextCh();
			for(;;) {
				if (ch == 10) {
					level--;
					if (level == 0) { oldEols = line - line0; NextCh(); return true; }
					NextCh();
				} else if (ch == Buffer.EOF) return false;
				else NextCh();
			}
		} else {
			buffer.Pos = pos0; NextCh(); line = line0; col = col0; charPos = charPos0;
		}
		return false;
	}

	bool Comment2() {
		int level = 1, pos0 = pos, line0 = line, col0 = col, charPos0 = charPos;
		NextCh();
		if (ch == '*') {
			NextCh();
			for(;;) {
				if (ch == '*') {
					NextCh();
					if (ch == '/') {
						level--;
						if (level == 0) { oldEols = line - line0; NextCh(); return true; }
						NextCh();
					}
				} else if (ch == '/') {
					NextCh();
					if (ch == '*') {
						level++; NextCh();
					}
				} else if (ch == Buffer.EOF) return false;
				else NextCh();
			}
		} else {
			buffer.Pos = pos0; NextCh(); line = line0; col = col0; charPos = charPos0;
		}
		return false;
	}


        void CheckLiteral() {
    		switch (t.val) {
			case "class": t.kind = 6; break;
			case "true": t.kind = 14; break;
			case "false": t.kind = 15; break;
			default: break;
		}
        }

        Token NextToken() {
            while (ch == ' ' ||
    			ch >= 9 && ch <= 10 || ch == 13
            ) NextCh();
    		if (ch == '#' && Comment0() ||ch == '/' && Comment1() ||ch == '/' && Comment2()) return NextToken();
            int recKind = noSym;
            int recEnd = pos;
            t = new Token();
            t.pos = pos; t.col = col; t.line = line; t.charPos = charPos;
            int state;
            if (start.ContainsKey(ch)) { state = (int) start[ch]; }
            else { state = 0; }
            tlen = 0; AddCh();
            
            switch (state) {
                case -1: { t.kind = eofSym; break; } // NextCh already done
                case 0: {
                    if (recKind != noSym) {
                        tlen = recEnd - t.pos;
                        SetScannerBehindT();
                    }
                    t.kind = recKind; break;
                } // NextCh already done
    			case 1:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 2;}
				else {goto case 0;}
			case 2:
				recEnd = pos; recKind = 1;
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 2;}
				else if (ch == '.') {AddCh(); goto case 3;}
				else {t.kind = 1; break;}
			case 3:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 4;}
				else {goto case 0;}
			case 4:
				recEnd = pos; recKind = 1;
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 4;}
				else {t.kind = 1; break;}
			case 5:
				if (ch >= '0' && ch <= '9' || ch >= 'A' && ch <= 'F' || ch >= 'a' && ch <= 'f') {AddCh(); goto case 6;}
				else {goto case 0;}
			case 6:
				recEnd = pos; recKind = 2;
				if (ch >= '0' && ch <= '9' || ch >= 'A' && ch <= 'F' || ch >= 'a' && ch <= 'f') {AddCh(); goto case 6;}
				else {t.kind = 2; break;}
			case 7:
				if (ch <= 9 || ch >= 11 && ch <= 12 || ch >= 14 && ch <= '!' || ch >= '#' && ch <= '[' || ch >= ']' && ch <= 65535) {AddCh(); goto case 7;}
				else if (ch == '"') {AddCh(); goto case 8;}
				else if (ch == 92) {AddCh(); goto case 12;}
				else {goto case 0;}
			case 8:
				{t.kind = 3; break;}
			case 9:
				recEnd = pos; recKind = 4;
				if (ch >= '0' && ch <= '9' || ch >= 'A' && ch <= 'Z' || ch == '_' || ch >= 'a' && ch <= 'z') {AddCh(); goto case 9;}
				else {t.kind = 4; break;}
			case 10:
				recEnd = pos; recKind = 5;
				if (ch >= '0' && ch <= '9' || ch >= 'A' && ch <= 'Z' || ch == '_' || ch >= 'a' && ch <= 'z') {AddCh(); goto case 10;}
				else {t.kind = 5; t.val = new String(tval, 0, tlen); CheckLiteral(); return t;}
			case 11:
				recEnd = pos; recKind = 1;
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 2;}
				else if (ch == '.') {AddCh(); goto case 3;}
				else if (ch == 'X' || ch == 'x') {AddCh(); goto case 5;}
				else {t.kind = 1; break;}
			case 12:
				if (ch <= 9 || ch >= 11 && ch <= 12 || ch >= 14 && ch <= '!' || ch >= '#' && ch <= '[' || ch >= ']' && ch <= 65535) {AddCh(); goto case 7;}
				else if (ch == '"') {AddCh(); goto case 13;}
				else if (ch == 92) {AddCh(); goto case 12;}
				else {goto case 0;}
			case 13:
				recEnd = pos; recKind = 3;
				if (ch <= 9 || ch >= 11 && ch <= 12 || ch >= 14 && ch <= '!' || ch >= '#' && ch <= '[' || ch >= ']' && ch <= 65535) {AddCh(); goto case 7;}
				else if (ch == '"') {AddCh(); goto case 8;}
				else if (ch == 92) {AddCh(); goto case 12;}
				else {t.kind = 3; break;}
			case 14:
				{t.kind = 7; break;}
			case 15:
				{t.kind = 8; break;}
			case 16:
				{t.kind = 9; break;}
			case 17:
				{t.kind = 10; break;}
			case 18:
				{t.kind = 11; break;}
			case 19:
				{t.kind = 12; break;}
			case 20:
				{t.kind = 13; break;}
			case 21:
				{t.kind = 16; break;}

            }
            t.val = new String(tval, 0, tlen);
            return t;
        }
        
        private void SetScannerBehindT() {
            buffer.Pos = t.pos;
            NextCh();
            line = t.line; col = t.col; charPos = t.charPos;
            for (int i = 0; i < tlen; i++) NextCh();
        }
        
        // get the next token (possibly a token already seen during peeking)
        public Token Scan () {
            if (tokens.next == null) {
                return NextToken();
            } else {
                pt = tokens = tokens.next;
                return tokens;
            }
        }

        // peek for the next token, ignore pragmas
        public Token Peek () {
            do {
                if (pt.next == null) {
                    pt.next = NextToken();
                }
                pt = pt.next;
            } while (pt.kind > maxT); // skip pragmas
        
            return pt;
        }

        // make sure that peeking starts at the current scan position
        public void ResetPeek () { pt = tokens; }

    } // end Scanner
}