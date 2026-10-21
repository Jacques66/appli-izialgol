program sets(output);
type color = (red, orange, yellow, green, blue, indigo, violet);
     colors = set of color;
var a, b, c: colors; s, t: set of 0..58; ch: char; cs: set of char; x: color; i: integer;
    small: set of 1..10;
procedure show(s: colors);
var c: color;
begin
   write('[');
   for c := red to violet do if c in s then write(ord(c):2);
   writeln(' ]')
end;
begin
   a := [red, yellow..blue]; b := [green..violet]; c := [];
   show(a); show(b); show(a + b); show(a * b); show(a - b); show(c);
   writeln(a = b, a <> b, a * b <= b, b >= a * b, [] <= a, a <= [], [red] = [red..red]);
   s := [0, 58]; t := [0..58];
   writeln(58 in s, 57 in s, 0 in t, (t - s) = [1..57]);
   i := 3; s := [i, i * 2, i * 3 .. i * 4]; for i := 0 to 58 do if i in s then write(i:3); writeln;
   cs := ['a'..'z', '0'..'9', '_'];
   for ch := ' ' to '~' do if not (ch in cs) and (ch in ['!'..'/']) then write(ch); writeln;
   writeln('x' in cs, 'X' in cs, '5' in cs);
   x := green; writeln(x in a, x in [red, blue], succ(x) in a);
   small := [1..10]; small := small - [2..9]; writeln(1 in small, 5 in small, 10 in small);
   writeln([1, 2] + [3] = [1..3], [5..1] = [])
end.
