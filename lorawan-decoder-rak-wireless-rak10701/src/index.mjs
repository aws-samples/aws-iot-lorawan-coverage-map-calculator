

function decodeUplink(bytes, fport) {
    //thanks to https://github.com/disk91/WioLoRaWANFieldTester/blob/master/doc/DEVELOPMENT.md#frame-format 
    var decoded = {};
    
    if (fport === 1) {
      var lonSign = (bytes[0]>>7) & 0x01 ? -1 : 1;
      var latSign = (bytes[0]>>6) & 0x01 ? -1 : 1;
      
      var encLat = ((bytes[0] & 0x3f)<<17)+
                   (bytes[1]<<9)+
                   (bytes[2]<<1)+
                   (bytes[3]>>7);
    
      var encLon = ((bytes[3] & 0x7f)<<16)+
                   (bytes[4]<<8)+
                   bytes[5];
      
      var hdop = bytes[8]/10;
      var sats = bytes[9];
      
      const maxHdop = 2;
      const minSats = 5;
      
      if ((hdop < maxHdop) && (sats >= minSats)) {
        // Send only acceptable quality of position to mappers
        decoded.latitude = latSign * (encLat * 108 + 53) / 10000000;
        decoded.longitude = lonSign * (encLon * 215 + 107) / 10000000;  
        decoded.altitude = ((bytes[6]<<8)+bytes[7])-1000;
        decoded.accuracy = (hdop*5+5)/10
        decoded.hdop = hdop;
        decoded.sats = sats;
      } else {
        decoded.error = "Need more GPS precision (hdop must be <"+maxHdop+
          " & sats must be >= "+minSats+") current hdop: "+hdop+" & sats:"+sats;
      }
      return decoded;
    }
      return null;
}

export const handler = async (event) => {
    try {
        const lorawan_info = event["WirelessMetadata"]["LoRaWAN"];
        const lorawan_data = event["PayloadData"];

        const lorawan_data_bytes = Buffer.from(lorawan_data, 'base64');
        
        console.info("Decoding Uplink for DevEUI: " + lorawan_info["DevEui"])

        //console.info("EVENT\n" + JSON.stringify(event, null, 2))

        const resolved_data = decodeUplink(lorawan_data_bytes, lorawan_info["FPort"]);
       
        console.info("resolved_data\n" + JSON.stringify(resolved_data, null, 2))
       
        var response = {
            IsValid: true,
            Battery: -1,
            PositionType: "Unknown",
            Position: {}
        };

        let unixTimeInSeconds = Math.floor(Date.now());
        response.Timestamp = unixTimeInSeconds;
        
        if ( resolved_data.error == null)
        {
            response.PositionType = "GPS",
            response.Position.Longitude = resolved_data.longitude;
            response.Position.Latitude = resolved_data.latitude;            
        }
        else
        {
            response.PositionType = "Unknown",
            response.ErrorMessage = resolved_data.error
        }      

        return response;
               
    } catch (error) {
        console.error('Error decoding message:', error);

        return {
            IsValid: false,
            PositionType: "Unknown",
            ErrorMessage: "Error decoding message (exception: " + error + ")"
        };
    }
};
