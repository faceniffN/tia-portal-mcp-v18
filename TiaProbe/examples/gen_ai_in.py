# -*- coding: utf-8 -*-
import io

def esc(s):
    return s.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;').replace('"', '&quot;')

doc_head = '''<?xml version="1.0" encoding="utf-8"?>
<Document>
  <Engineering version="V18" />
  <DocumentInfo>
    <Created>2026-09-26T08:00:00.0000000Z</Created>
    <ExportSetting>WithDefaults</ExportSetting>
    <InstalledProducts>
      <Product>
        <DisplayName>Totally Integrated Automation Portal</DisplayName>
        <DisplayVersion>V18</DisplayVersion>
      </Product>
      <OptionPackage>
        <DisplayName>TIA Portal Openness</DisplayName>
        <DisplayVersion>V18</DisplayVersion>
      </OptionPackage>
      <Product>
        <DisplayName>STEP 7 Professional</DisplayName>
        <DisplayVersion>V18</DisplayVersion>
      </Product>
    </InstalledProducts>
  </DocumentInfo>
'''

channels = []
for i in range(16):
    channels.append(("PT%02d" % (i+1), "PT", i))
for i in range(6):
    channels.append(("DT%02d" % (i+1), "DT", i))

def network(cid, src_name, arr_name, idx):
    parts = []
    parts.append('''    <Access Scope="GlobalVariable" UId="%d">
      <Symbol>
        <Component Name="%s" />
      </Symbol>
    </Access>''' % (cid+1, src_name))
    parts.append('''    <Access Scope="GlobalVariable" UId="%d">
      <Symbol>
        <Component Name="date" />
        <Component Name="%s" AccessModifier="Array">
          <Access Scope="LiteralConstant">
            <Constant>
              <ConstantType>DInt</ConstantType>
              <ConstantValue>%d</ConstantValue>
            </Constant>
          </Access>
        </Component>
        <Component Name="Swap" />
      </Symbol>
    </Access>''' % (cid+2, arr_name, idx))
    parts.append('''    <Part Name="Move" UId="%d" DisabledENO="true">
      <TemplateValue Name="Card" Type="Cardinality">1</TemplateValue>
    </Part>''' % (cid+3))
    wires = []
    wires.append('''    <Wire UId="%d">
      <Powerrail />
      <NameCon UId="%d" Name="en" />
    </Wire>''' % (cid+4, cid+3))
    wires.append('''    <Wire UId="%d">
      <IdentCon UId="%d" />
      <NameCon UId="%d" Name="in" />
    </Wire>''' % (cid+5, cid+1, cid+3))
    wires.append('''    <Wire UId="%d">
      <NameCon UId="%d" Name="out1" />
      <IdentCon UId="%d" />
    </Wire>''' % (cid+6, cid+3, cid+2))
    return '''<FlgNet xmlns="http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v4">
  <Parts>
%s
  </Parts>
  <Wires>
%s
  </Wires>
</FlgNet>''' % ("\n".join(parts), "\n".join(wires))

def compile_unit(cid, title, src, arr, idx):
    nl = network(cid, src, arr, idx)
    return '''      <SW.Blocks.CompileUnit ID="%d" CompositionName="CompileUnits">
        <AttributeList>
          <NetworkSource>%s</NetworkSource>
          <ProgrammingLanguage>LAD</ProgrammingLanguage>
        </AttributeList>
        <ObjectList>
          <MultilingualText ID="%d" CompositionName="Comment">
            <ObjectList>
              <MultilingualTextItem ID="%d" CompositionName="Items">
                <AttributeList>
                  <Culture>zh-CN</Culture>
                  <Text />
                </AttributeList>
              </MultilingualTextItem>
            </ObjectList>
          </MultilingualText>
          <MultilingualText ID="%d" CompositionName="Title">
            <ObjectList>
              <MultilingualTextItem ID="%d" CompositionName="Items">
                <AttributeList>
                  <Culture>zh-CN</Culture>
                  <Text>%s</Text>
                </AttributeList>
              </MultilingualTextItem>
            </ObjectList>
          </MultilingualText>
        </ObjectList>
      </SW.Blocks.CompileUnit>''' % (cid, nl, cid+10, cid+11, cid+12, cid+13, title)

cuid = 3
units = []
for (src, arr, idx) in channels:
    title = '%s_%s_%d_MOVE' % (src, arr, idx)
    units.append(compile_unit(cuid, title, src, arr, idx))
    cuid += 20

body = '''
  <SW.Blocks.FC ID="0">
    <AttributeList>
      <AutoNumber>true</AutoNumber>
      <HeaderAuthor />
      <HeaderFamily />
      <HeaderName />
      <HeaderVersion>0.1</HeaderVersion>
      <Interface><Sections xmlns="http://www.siemens.com/automation/Openness/SW/Interface/v5">
  <Section Name="Input" />
  <Section Name="Output" />
  <Section Name="InOut" />
  <Section Name="Temp" />
  <Section Name="Constant" />
  <Section Name="Return">
    <Member Name="Ret_Val" Datatype="Void" Accessibility="Public" />
  </Section>
</Sections></Interface>
      <IsIECCheckEnabled>false</IsIECCheckEnabled>
      <MemoryLayout>Optimized</MemoryLayout>
      <Name>AI_IN</Name>
      <Namespace />
      <Number>100</Number>
      <ProgrammingLanguage>LAD</ProgrammingLanguage>
      <SetENOAutomatically>false</SetENOAutomatically>
      <UDABlockProperties />
      <UDAEnableTagReadback>false</UDAEnableTagReadback>
    </AttributeList>
    <ObjectList>
      <MultilingualText ID="1" CompositionName="Comment">
        <ObjectList>
          <MultilingualTextItem ID="2" CompositionName="Items">
            <AttributeList>
              <Culture>zh-CN</Culture>
              <Text />
            </AttributeList>
          </MultilingualTextItem>
        </ObjectList>
      </MultilingualText>
%s
    </ObjectList>
  </SW.Blocks.FC>
</Document>''' % "\n".join(units)

out = doc_head + body
path = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01Assets\plc_blocks\AI_IN.xml"
with io.open(path, "w", encoding="utf-8") as f:
    f.write(out)
print("生成完成:", path, "网络数:", len(channels))
