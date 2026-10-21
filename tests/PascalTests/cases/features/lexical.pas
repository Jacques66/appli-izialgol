{ Lexical conventions: case-insensitive words, both kinds of comments,
  alternative symbols (. .) for [ ], @ and ^ for pointers, doubled quotes. }
PROGRAM Lexical(OUTPUT);
VAR A: ARRAY(.1..3.) OF Integer; P: @Integer; Q: ^integer; s: packed array[1..6] of char;
BEGIN
   a(.1.) := 10; A[2] := 20; a[3] := a(.1.) + A(.2.);   (* old style comment *)
   WriteLn(a[3]:1, ' ', A[1]:1);
   new(p); p@ := 5; q := p; WRITELN(q^:1);
   s := 'it''s a'; writeln(s, ' ', '''', ' ', 'a''b');
   writeln(1.5e3:8:1, 2E2:7:1, 0.25E-1:8:4, 7e+0:5:1);
   writeln(ord('A'):1, ord('a'):1, ord(' '):3, ord('0'):3)
END.
