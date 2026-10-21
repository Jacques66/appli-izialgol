program arrays(output);
type color = (red, green, blue);
     row = array[1..3] of integer;
     grid = array[1..3] of row;
var g, h: grid; i, j: integer;
    counts: array[color] of integer;
    flags: array[boolean] of char;
    alpha: array['a'..'e'] of integer;
    m: array[1..2, 1..2, 1..2] of integer;
    c: color; ch: char;
begin
   for i := 1 to 3 do for j := 1 to 3 do g[i][j] := 10 * i + j;
   h := g; h[2, 2] := 0;
   writeln(g[2, 2]:3, h[2][2]:3, h[3, 1]:3);
   h[1] := g[3]; writeln(h[1, 1]:3, h[1, 3]:3);
   for c := red to blue do counts[c] := ord(c) * 2;
   writeln(counts[green]:2, counts[blue]:2);
   flags[false] := 'n'; flags[true] := 'y'; writeln(flags[3 > 2], flags[1 > 2]);
   for ch := 'a' to 'e' do alpha[ch] := ord(ch) - ord('a');
   writeln(alpha['d']:2);
   for i := 1 to 2 do for j := 1 to 2 do begin m[i, j, 1] := i + j; m[i][j][2] := i * j end;
   writeln(m[2, 2, 1]:2, m[2, 1][2]:2)
end.
