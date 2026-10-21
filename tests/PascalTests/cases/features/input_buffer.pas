program inputbuffer(input, output);
var count: integer;
begin
   count := 0;
   while not eof(input) do
   begin
      while not eoln(input) do
      begin
         if input^ <> ' ' then begin write(input^); count := count + 1 end;
         get(input)
      end;
      writeln('/');
      get(input)
   end;
   writeln(count:1, ' non-blank characters')
end.
