program widths(output);
var c: char;
begin
   writeln('[', 42, ']');
   writeln('[', 42:1, '][', 42:5, '][', -42:2, '][', 12345:3, ']');
   c := 'z';
   writeln('[', c, '][', c:3, '][', 'abc':5, '][', 'abcdef':3, ']');
   writeln('[', true:2, '][', false:7, ']');
   write('a'); write('b', 'c'); writeln;
   writeln;
   writeln('end')
end.
