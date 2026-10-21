{ Heapsort (R. W. Floyd), as in N. Wirth, Algorithms + Data Structures =
  Programs: sorts a table of names (packed arrays of char, compared with
  the relational operators) in place. }
program heapsort(output);
const n = 12;
type name = packed array[1..8] of char;
var a: array[1..n] of name;
    i, l, r: integer;
    x: name;

procedure sift(l, r: integer);
label 13;
var i, j: integer;
    x: name;
begin
   i := l; j := 2 * i; x := a[i];
   while j <= r do
   begin
      if j < r then
         if a[j] < a[j + 1] then j := j + 1;
      if x >= a[j] then goto 13;
      a[i] := a[j]; i := j; j := 2 * i
   end;
13: a[i] := x
end;

begin
   a[1] := 'Wirth   '; a[2] := 'Hoare   '; a[3] := 'Dijkstra'; a[4] := 'Naur    ';
   a[5] := 'Floyd   '; a[6] := 'Knuth   '; a[7] := 'Jensen  '; a[8] := 'Ammann  ';
   a[9] := 'Backus  '; a[10] := 'Dahl    '; a[11] := 'Nygaard '; a[12] := 'Hoare   ';
   l := (n div 2) + 1; r := n;
   while l > 1 do begin l := l - 1; sift(l, r) end;
   while r > 1 do
   begin
      x := a[1]; a[1] := a[r]; a[r] := x;
      r := r - 1; sift(l, r)
   end;
   for i := 1 to n do writeln(i:2, '  ', a[i])
end.
